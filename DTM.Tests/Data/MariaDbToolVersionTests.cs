using DTM.Data.MariaDb;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Data;

/// <summary>
/// Die Versionszeilen stammen aus echten Installationen. Das Parsen ist die
/// Stelle, an der ein Fehler still bleibt: eine falsch gelesene Version führt
/// entweder zu einer Warnung, die niemand braucht, oder — schlimmer — zu
/// keiner Warnung, wo eine nötig wäre.
/// </summary>
public class MariaDbToolVersionParseTests
{
    [Theory]
    // MariaDB: "Ver 10.19" ist die interne Nummer des Werkzeugs, "Distrib
    // 10.11.6" die Produktversion. Wer die erste vergleicht, hält eine
    // MariaDB 10.11 für eine 10.19 — und die Prüfung gegen den Server kippt
    // ins Gegenteil.
    [InlineData("mariadb-dump  Ver 10.19 Distrib 10.11.6-MariaDB, for Linux (x86_64)", 10, 11, 6)]
    [InlineData("mysqldump  Ver 10.19 Distrib 10.6.16-MariaDB, for debian-linux-gnu (x86_64)", 10, 6, 16)]
    // Ältere MySQL-Reihen tragen ebenfalls Distrib.
    [InlineData("mysqldump  Ver 10.13 Distrib 5.7.44, for Linux (x86_64)", 5, 7, 44)]
    // Ab MySQL 8 fällt Distrib weg — dort IST "Ver" die Produktversion.
    [InlineData("mysqldump  Ver 8.0.35 for Linux on x86_64 (MySQL Community Server - GPL)", 8, 0, 35)]
    public void Parse_ReadsTheProductVersion(string ausgabe, int major, int minor, int build)
    {
        DumpToolInfo? info = MariaDbToolVersion.Parse(ausgabe);

        info.Should().NotBeNull();
        info!.Version.Major.Should().Be(major);
        info.Version.Minor.Should().Be(minor);
        info.Version.Build.Should().Be(build);
        info.RawOutput.Should().Be(ausgabe);
    }

    [Theory]
    [InlineData("mariadb-dump  Ver 10.19 Distrib 10.11.6-MariaDB, for Linux", DumpToolFlavour.MariaDb)]
    [InlineData("mysqldump  Ver 8.0.35 for Linux (MySQL Community Server - GPL)", DumpToolFlavour.MySql)]
    [InlineData("mysqldump  Ver 10.13 Distrib 5.7.44, for Linux", DumpToolFlavour.MySql)]
    public void Parse_RecognisesTheFlavour(string ausgabe, DumpToolFlavour erwartet)
    {
        MariaDbToolVersion.Parse(ausgabe)!.Flavour.Should().Be(erwartet);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("irgendwas ohne Versionsangabe")]
    public void Parse_WithoutAVersion_ReturnsNull(string? ausgabe)
    {
        // Lieber nichts als geraten: Check() meldet dann „nicht lesbar" statt
        // eine erfundene Zahl zu vergleichen.
        MariaDbToolVersion.Parse(ausgabe).Should().BeNull();
    }

    [Theory]
    [InlineData("10.11.6-MariaDB-0+deb12u1", 10, 11, 6)]
    [InlineData("10.6.16-MariaDB-log", 10, 6, 16)]
    [InlineData("8.0.35", 8, 0, 35)]
    [InlineData("5.7.44-0ubuntu0.18.04.1", 5, 7, 44)]
    public void ParseServerVersion_StripsDistributionSuffix(string gemeldet, int major, int minor, int build)
    {
        Version? v = MariaDbToolVersion.ParseServerVersion(gemeldet);

        v.Should().NotBeNull();
        v!.Major.Should().Be(major);
        v.Minor.Should().Be(minor);
        v.Build.Should().Be(build);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unbekannt")]
    public void ParseServerVersion_Unreadable_ReturnsNull(string? gemeldet)
    {
        MariaDbToolVersion.ParseServerVersion(gemeldet).Should().BeNull();
    }
}

/// <summary>
/// Der Vergleich selbst. Zwei Dinge sollen gemeldet werden — ein zu altes
/// Werkzeug gegenüber DTM, und ein Werkzeug, das älter ist als der Server.
/// Alles andere soll schweigen: eine Warnung, die bei jedem Dump erscheint,
/// wird nach dem dritten Mal nicht mehr gelesen.
/// </summary>
public class MariaDbToolVersionCheckTests
{
    private static DumpToolInfo Werkzeug(string version, DumpToolFlavour flavour = DumpToolFlavour.MariaDb) =>
        new(flavour, Version.Parse(version), $"mariadb-dump Ver … Distrib {version}");

    [Fact]
    public void Check_ToolMatchesServer_SaysNothing()
    {
        MariaDbToolVersion.Check(Werkzeug("10.11.6"), "10.11.9-MariaDB", "mariadb-dump")
            .Should().BeNull("Korrekturstände dürfen auseinanderlaufen, das Dumpformat ändert sich dabei nicht");
    }

    [Fact]
    public void Check_ToolNewerThanServer_SaysNothing()
    {
        // Der Normalfall nach einem Client-Update und völlig unkritisch.
        MariaDbToolVersion.Check(Werkzeug("11.4.2"), "10.11.6-MariaDB", "mariadb-dump")
            .Should().BeNull();
    }

    [Fact]
    public void Check_ToolOlderThanServer_Warns()
    {
        // Der Fall, der in der Praxis weh tut: der Dump meldet Erfolg, der
        // Restore scheitert.
        string? meldung = MariaDbToolVersion.Check(
            Werkzeug("5.7.44", DumpToolFlavour.MySql), "10.11.6-MariaDB", "mysqldump");

        meldung.Should().NotBeNull();
        meldung.Should().StartWith("VERSION_MISMATCH:");
        meldung.Should().Contain("5.7.44").And.Contain("10.11.6-MariaDB");
        meldung.Should().Contain("Restore", "die Meldung muss sagen, was konkret schiefgeht");
    }

    [Fact]
    public void Check_ToolBelowMinimum_Warns()
    {
        string? meldung = MariaDbToolVersion.Check(Werkzeug("5.5.68"), "5.5.68-MariaDB", "mariadb-dump");

        meldung.Should().NotBeNull();
        meldung.Should().StartWith("VERSION_MISMATCH:");
        meldung.Should().Contain(MariaDbToolVersion.RequiredMariaDbVersion.ToString());
    }

    [Fact]
    public void Check_MySqlFlavour_UsesItsOwnMinimum()
    {
        // 5.7 liegt unter der MariaDB-Untergrenze von 10.1, ist für den
        // MySQL-Zweig aber genau die Untergrenze. Ohne die Fallunterscheidung
        // würde jede MySQL-Installation grundlos gemeldet.
        MariaDbToolVersion.Check(Werkzeug("5.7.44", DumpToolFlavour.MySql), "5.7.44", "mysqldump")
            .Should().BeNull();
    }

    [Fact]
    public void Check_UnreadableToolVersion_SaysSoWithoutGuessing()
    {
        string? meldung = MariaDbToolVersion.Check(null, "10.11.6-MariaDB", @"C:\tools\mariadb-dump.exe");

        meldung.Should().NotBeNull();
        meldung.Should().StartWith("VERSION_UNBEKANNT:");
        meldung.Should().Contain("mariadb-dump.exe", "der Dateiname gehört in die Meldung");
        meldung.Should().NotContain("VERSION_MISMATCH", "ungelesen ist nicht dasselbe wie unpassend");
    }

    [Fact]
    public void Check_UnreadableServerVersion_DoesNotWarn()
    {
        // Der Server sagt nichts Brauchbares — dann ist der Vergleich gegen
        // ihn nicht möglich. Die Untergrenze wurde vorher schon geprüft.
        MariaDbToolVersion.Check(Werkzeug("10.11.6"), null, "mariadb-dump")
            .Should().BeNull();
    }

    /// <summary>
    /// Der Grund, warum der Vergleich nicht auf dem Versionsstring arbeitet:
    /// als Text ist „10.9" größer als „10.11", als Version kleiner. Genau
    /// dieser Dreher hat in der Einrichtungs-SQL des Server-Kits schon eine
    /// Rolle gespielt.
    /// </summary>
    [Fact]
    public void Check_ComparesNumerically_NotAsText()
    {
        MariaDbToolVersion.Check(Werkzeug("10.9.8"), "10.11.6-MariaDB", "mariadb-dump")
            .Should().NotBeNull("10.9 ist älter als 10.11, auch wenn es als Text größer aussieht");

        MariaDbToolVersion.Check(Werkzeug("10.11.6"), "10.9.8-MariaDB", "mariadb-dump")
            .Should().BeNull("10.11 ist neuer als 10.9");
    }
}
