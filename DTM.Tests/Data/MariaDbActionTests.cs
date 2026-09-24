using DTM.Config;
using DTM.Data.MariaDb;
using DTM.MariaDb;
using FluentAssertions;
using Xunit;
using SystemFile = System.IO.File;

namespace DTM.Tests.Data;

/// <summary>
/// Das Quoten von Bezeichnern ist die einzige Stelle, an der ein Name
/// ungeprüft in den SQL-Text wandert — Tabellen- und Datenbanknamen lassen
/// sich nicht als Parameter binden. Entsprechend genau getestet.
/// </summary>
public class MariaDbQuotingTests
{
    [Theory]
    [InlineData("kunden", "`kunden`")]
    [InlineData("Tabelle mit Leerzeichen", "`Tabelle mit Leerzeichen`")]
    [InlineData("mit-Bindestrich", "`mit-Bindestrich`")]
    public void QuoteIdentifier_WrapsInBackticks(string input, string expected)
    {
        MariaDbActionService.QuoteIdentifier(input).Should().Be(expected);
    }

    [Fact]
    public void QuoteIdentifier_DoublesEmbeddedBacktick()
    {
        // Die von MariaDB vorgesehene Escape-Regel. Ohne sie könnte ein Name
        // mit Backtick aus dem Bezeichner ausbrechen und eigenes SQL anhängen.
        MariaDbActionService.QuoteIdentifier("bo`se").Should().Be("`bo``se`");
    }

    [Fact]
    public void QuoteIdentifier_InjectionAttempt_StaysOneIdentifier()
    {
        string evil = "x`; DROP TABLE kunden; --";

        string quoted = MariaDbActionService.QuoteIdentifier(evil);

        quoted.Should().StartWith("`").And.EndWith("`");
        // Entscheidend: kein unmaskierter Backtick im Inneren — sonst wäre
        // der Bezeichner vorzeitig zu Ende.
        quoted[1..^1].Replace("``", string.Empty, StringComparison.Ordinal)
            .Should().NotContain("`");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void QuoteIdentifier_RejectsEmpty(string? input)
    {
        Action act = () => MariaDbActionService.QuoteIdentifier(input!);

        act.Should().Throw<ArgumentException>();
    }
}

public class MariaDbBackupServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "dtm-mariadb-" + Guid.NewGuid().ToString("N"));

    public MariaDbBackupServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private MariaDbBackupService Service(string? backupRoot = null) =>
        new(new MariaDb_Connector(new ServerCredential("db01", "root", "geheim")),
            new MariaDbSettings { BackupRoot = backupRoot ?? _dir });

    // --- Werkzeug-Auflösung ---

    [Fact]
    public void ResolveTool_ConfiguredPath_IsUsed()
    {
        string exe = Path.Combine(_dir, "mariadb-dump.exe");
        SystemFile.WriteAllText(exe, "x");

        MariaDbBackupService.ResolveTool(exe, ["mariadb-dump"], "mariadb-dump")
            .Should().Be(exe);
    }

    [Fact]
    public void ResolveTool_ConfiguredButMissing_ThrowsPointingAtSettings()
    {
        string missing = Path.Combine(_dir, "gibtsnicht.exe");

        Action act = () => MariaDbBackupService.ResolveTool(missing, ["mariadb-dump"], "mariadb-dump");

        act.Should().Throw<FileNotFoundException>()
            .WithMessage("*Einstellungen*");
    }

    [Fact]
    public void ResolveTool_NotFoundAnywhere_NamesTheCandidates()
    {
        // Die Meldung muss sagen, wonach gesucht wurde — sonst rät der
        // Nutzer, welches Werkzeug er installieren soll.
        Action act = () => MariaDbBackupService.ResolveTool(
            string.Empty, ["gibt-es-nicht-xyz", "auch-nicht-abc"], "mariadb-dump");

        act.Should().Throw<FileNotFoundException>()
            .WithMessage("*gibt-es-nicht-xyz*auch-nicht-abc*");
    }

    // --- Verzeichnis-Aufbau ---

    [Fact]
    public void BackupDirectory_SeparatesByServerAndDatabase()
    {
        // Gleichnamige Datenbanken auf verschiedenen Servern dürfen sich
        // nicht im selben Ordner überschreiben.
        string dir = Service().BackupDirectoryFor("kunden");

        dir.Should().StartWith(_dir);
        dir.Should().EndWith(Path.Combine("db01", "kunden"));
    }

    [Fact]
    public void BackupDirectory_EmptyRoot_FallsBackToUserProfile()
    {
        string dir = Service(backupRoot: string.Empty).BackupDirectoryFor("kunden");

        dir.Should().Contain("DTM-Backups");
    }

    [Theory]
    [InlineData("db/mit/slash")]
    [InlineData("db:mit:doppelpunkt")]
    [InlineData("db*mit?wildcards")]
    public void SanitizeForPath_ReplacesInvalidCharacters(string raw)
    {
        string safe = MariaDbBackupService.SanitizeForPath(raw);

        safe.Should().NotContainAny(Path.GetInvalidFileNameChars().Select(c => c.ToString()));
        safe.Should().HaveLength(raw.Length, "es wird ersetzt, nicht entfernt");
    }

    [Fact]
    public void ListBackups_MissingDirectory_IsEmpty()
    {
        Service(Path.Combine(_dir, "nicht-da")).ListBackups("kunden").Should().BeEmpty();
    }

    [Fact]
    public void ListBackups_ReturnsNewestFirst()
    {
        MariaDbBackupService svc = Service();
        string dir = svc.BackupDirectoryFor("kunden");
        Directory.CreateDirectory(dir);

        string alt = Path.Combine(dir, "kunden-20200101_1000.sql");
        string neu = Path.Combine(dir, "kunden-20250101_1000.sql");
        SystemFile.WriteAllText(alt, "alt");
        SystemFile.WriteAllText(neu, "neu");
        SystemFile.SetCreationTimeUtc(alt, new DateTime(2020, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        SystemFile.SetCreationTimeUtc(neu, new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc));

        var list = svc.ListBackups("kunden");

        list.Should().HaveCount(2);
        list[0].FileName.Should().Be("kunden-20250101_1000.sql");
    }

    [Fact]
    public void ListBackups_IgnoresNonSqlFiles()
    {
        MariaDbBackupService svc = Service();
        string dir = svc.BackupDirectoryFor("kunden");
        Directory.CreateDirectory(dir);
        SystemFile.WriteAllText(Path.Combine(dir, "notizen.txt"), "x");
        SystemFile.WriteAllText(Path.Combine(dir, "kunden-20250101_1000.sql"), "x");

        svc.ListBackups("kunden").Should().ContainSingle()
            .Which.FileName.Should().EndWith(".sql");
    }

    [Fact]
    public void ListBackups_ReportsExactByteSize()
    {
        // Die Größe wird hier bewusst nicht in MB vorgerundet: der
        // Backup-Browser bereitet sie selbst auf, und eine vorab gerundete
        // Zahl ließe sich nicht mehr exakt zurückrechnen.
        MariaDbBackupService svc = Service();
        string dir = svc.BackupDirectoryFor("kunden");
        Directory.CreateDirectory(dir);
        SystemFile.WriteAllBytes(Path.Combine(dir, "kunden-20250101_1000.sql"), new byte[1234]);

        svc.ListBackups("kunden").Single().SizeBytes.Should().Be(1234);
    }

    [Fact]
    public async Task RestoreAsync_MissingFile_ThrowsBeforeStartingAnything()
    {
        MariaDbBackupService svc = Service();

        Func<Task> act = () => svc.RestoreAsync("kunden", Path.Combine(_dir, "gibtsnicht.sql"));

        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}
