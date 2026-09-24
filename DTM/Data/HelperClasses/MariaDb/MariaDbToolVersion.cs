using System.Text.RegularExpressions;

namespace DTM.Data.MariaDb;

/// <summary>Welches Werkzeug gefunden wurde — die beiden verhalten sich nicht gleich.</summary>
public enum DumpToolFlavour
{
    /// <summary>Aus der Versionsangabe nicht ableitbar.</summary>
    Unbekannt,
    MariaDb,
    MySql,
}

/// <summary>
/// Die aus <c>mariadb-dump --version</c> gelesene Fassung des Werkzeugs.
/// </summary>
/// <param name="Flavour">MariaDB oder MySQL.</param>
/// <param name="Version">Die Version der Distribution, nicht die interne
/// Werkzeug-Nummer — siehe <see cref="MariaDbToolVersion.Parse"/>.</param>
/// <param name="RawOutput">Die Zeile, wie das Werkzeug sie ausgegeben hat.
/// Gehört in jede Meldung, damit man nicht raten muss, was gefunden wurde.</param>
public sealed record DumpToolInfo(DumpToolFlavour Flavour, Version Version, string RawOutput);

/// <summary>
/// Prüft, ob das gefundene Dump-Werkzeug zu DTM und zum Server passt — das
/// Gegenstück zum <c>VERSION_MISMATCH</c>, mit dem FOC-SQL ein veraltetes
/// MSSQL-Modul auf einem Server meldet.
///
/// <para>Der Unterschied zur MSSQL-Seite: dort liegt das Modul auf dem Server
/// und DTM prüft aus der Ferne. Hier liegt das Werkzeug auf dem Arbeitsplatz,
/// und geprüft wird gegen zwei Größen — die in DTM hinterlegte Untergrenze und
/// die Version des Servers.</para>
///
/// <para>Die zweite Prüfung ist die, die in der Praxis zuschlägt: ein
/// Dump-Werkzeug, das älter ist als der Server, erzeugt Dumps, die sich nicht
/// zuverlässig zurückspielen lassen. Der Klassiker ist <c>mysqldump</c> aus
/// MySQL 5.7 gegen eine MariaDB 10.11 — der Lauf meldet Erfolg, der Restore
/// scheitert.</para>
/// </summary>
public static class MariaDbToolVersion
{
    /// <summary>
    /// Untergrenze für das Dump-Werkzeug. 10.1 ist die erste MariaDB-Reihe, in
    /// der alle Optionen vorhanden sind, auf die DTM sich verlässt
    /// (<c>--single-transaction</c> zusammen mit <c>--routines --events
    /// --triggers</c> und <c>--default-character-set=utf8mb4</c>).
    /// </summary>
    public static readonly Version RequiredMariaDbVersion = new(10, 1);

    /// <summary>
    /// Dieselbe Untergrenze für den MySQL-Zweig. MySQL 5.7 kennt utf8mb4, die
    /// Reihen davor nicht durchgängig.
    /// </summary>
    public static readonly Version RequiredMySqlVersion = new(5, 7);

    // "mariadb-dump  Ver 10.19 Distrib 10.11.6-MariaDB, for Linux (x86_64)"
    // "mysqldump  Ver 10.13 Distrib 5.7.44, for Linux (x86_64)"
    // Bei beiden ist "Distrib" die Version, die zählt. "Ver" davor ist die
    // interne Nummer des Werkzeugs selbst und hat mit der Server-Reihe nichts
    // zu tun — wer die vergleicht, hält eine MariaDB 10.11 für eine 10.19.
    private static readonly Regex DistribPattern = new(
        @"Distrib\s+(?<v>\d+(?:\.\d+){0,3})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "mysqldump  Ver 8.0.35 for Linux on x86_64 (MySQL Community Server - GPL)"
    // Ab MySQL 8 fällt "Distrib" weg, dort IST "Ver" die Produktversion.
    private static readonly Regex VerPattern = new(
        @"\bVer\s+(?<v>\d+(?:\.\d+){0,3})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Liest die Version aus der Ausgabe von <c>--version</c>.
    /// Liefert <c>null</c>, wenn sich nichts erkennen lässt — dann wird nicht
    /// geraten, sondern die Prüfung übersprungen und der Rohtext gemeldet.
    /// </summary>
    public static DumpToolInfo? Parse(string? versionOutput)
    {
        if (string.IsNullOrWhiteSpace(versionOutput)) return null;

        string line = versionOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? string.Empty;
        if (line.Length == 0) return null;

        DumpToolFlavour flavour =
            line.Contains("MariaDB", StringComparison.OrdinalIgnoreCase) ? DumpToolFlavour.MariaDb
            : line.Contains("MySQL", StringComparison.OrdinalIgnoreCase)
              || line.Contains("mysqldump", StringComparison.OrdinalIgnoreCase) ? DumpToolFlavour.MySql
            : DumpToolFlavour.Unbekannt;

        // Distrib zuerst: steht beides in der Zeile, ist Distrib die richtige.
        Match m = DistribPattern.Match(line);
        if (!m.Success) m = VerPattern.Match(line);
        if (!m.Success) return null;

        return Version.TryParse(Normalise(m.Groups["v"].Value), out Version? v)
            ? new DumpToolInfo(flavour, v, line)
            : null;
    }

    /// <summary>
    /// Liest die Version aus dem, was der Server über <c>SELECT VERSION()</c>
    /// meldet — etwa <c>10.11.6-MariaDB-0+deb12u1</c>. Alles ab dem ersten
    /// Bindestrich ist Distributions-Beiwerk und stört beim Vergleich.
    /// </summary>
    public static Version? ParseServerVersion(string? serverVersion)
    {
        if (string.IsNullOrWhiteSpace(serverVersion)) return null;

        string zahlen = new(serverVersion.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return Version.TryParse(Normalise(zahlen), out Version? v) ? v : null;
    }

    /// <summary>
    /// <c>Version.TryParse</c> verlangt mindestens zwei Bestandteile: aus "10"
    /// allein wird sonst nichts. Kommt in freier Wildbahn bei abgeschnittenen
    /// Angaben vor.
    /// </summary>
    private static string Normalise(string raw) =>
        raw.Contains('.', StringComparison.Ordinal) ? raw : raw + ".0";

    /// <summary>
    /// Vergleicht das Werkzeug gegen die Untergrenze und gegen den Server.
    /// Liefert <c>null</c>, wenn alles passt, sonst den fertigen Meldungstext
    /// für Statusleiste und Konsole.
    /// </summary>
    /// <param name="tool">Ergebnis von <see cref="Parse"/>, oder <c>null</c>.</param>
    /// <param name="serverVersion">Was der Server über <c>VERSION()</c> meldet.</param>
    /// <param name="toolPath">Pfad zum Werkzeug, für die Meldung.</param>
    public static string? Check(DumpToolInfo? tool, string? serverVersion, string toolPath)
    {
        string name = Path.GetFileName(toolPath);

        if (tool is null)
        {
            return $"VERSION_UNBEKANNT: Die Version von '{name}' liess sich nicht lesen. "
                 + "Der Dump läuft trotzdem, aber ob das Werkzeug zum Server passt, ist ungeprüft.";
        }

        Version untergrenze = tool.Flavour == DumpToolFlavour.MySql
            ? RequiredMySqlVersion
            : RequiredMariaDbVersion;

        if (tool.Version < untergrenze)
        {
            return $"VERSION_MISMATCH: '{name}' ist Version {tool.Version} — DTM verlangt mindestens "
                 + $"{untergrenze}. Ältere Fassungen kennen die Optionen nicht, mit denen DTM sichert; "
                 + "der Dump wäre unvollständig. Bitte die Client-Werkzeuge aktualisieren.";
        }

        Version? server = ParseServerVersion(serverVersion);
        if (server is null) return null;

        // Nur Haupt- und Nebenversion vergleichen. Eine 10.11.6 gegen einen
        // Server 10.11.9 ist in Ordnung — Korrekturstände laufen auseinander,
        // ohne dass das Dumpformat sich ändert.
        Version toolReihe = new(tool.Version.Major, tool.Version.Minor);
        Version serverReihe = new(server.Major, server.Minor);

        if (toolReihe < serverReihe)
        {
            return $"VERSION_MISMATCH: '{name}' ist Version {tool.Version}, der Server läuft auf "
                 + $"{serverVersion}. Ein älteres Dump-Werkzeug erzeugt Dumps, die sich nicht "
                 + "zuverlässig zurückspielen lassen — der Lauf meldet Erfolg, der Restore scheitert. "
                 + "Bitte die Client-Werkzeuge auf die Server-Reihe heben.";
        }

        // Umgekehrter Fall — Werkzeug neuer als Server — ist unkritisch und
        // der Normalfall nach einem Client-Update. Keine Meldung.
        return null;
    }
}
