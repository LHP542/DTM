using System.Text.RegularExpressions;
using NLog;

namespace DTM.Updater;

/// <summary>
/// Reine Logik rund um den Update-Kanal — bewusst ohne Netz- und Dateizugriff
/// im Kern, damit sie sich ohne Share und ohne GitHub testen laesst.
///
/// <para>DTM kennt zwei Kanaltypen: einen <b>Ordner im Netz</b> (Standard, weil
/// DTM ein dienstliches Werkzeug im Firmennetz ist) und <b>GitHub Releases</b>
/// (fuer Entwicklung ausserhalb des Netzes). Welcher gilt, wird an der
/// Schreibweise erkannt — nicht an einem zweiten Schalter. Ein UNC-Pfad und
/// eine URL sind nicht zu verwechseln, und eine Einstellung mehr waere eine
/// mehr, die jemand falsch setzt.</para>
/// </summary>
public static class UpdateChannel
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Standard-Kanal: das Rollout-Verzeichnis im Firmennetz. Greift, solange
    /// in den Einstellungen nichts anderes steht.
    /// </summary>
    public const string DefaultFolder = @"\\samba01\542$\5424_IT-Basis-Dienste\MS-SQL\DTM";

    /// <summary>Dateiname der Release-Hinweise im Kanal-Ordner.</summary>
    public const string ReleaseNotesFileName = "release-notes.json";

    /// <summary>Erste Versionsangabe in einem Dateinamen, z. B. <c>DTM-v2.3.11-windows.zip</c>.</summary>
    private static readonly Regex VersionInName =
        new(@"(\d+\.\d+\.\d+(?:\.\d+)?)", RegexOptions.Compiled);

    /// <summary>
    /// Ist die Kanal-Angabe ein Ordner statt einer Adresse? Erkannt werden
    /// UNC (<c>\\server\share</c>), Windows-Laufwerke (<c>D:\…</c>) und
    /// absolute Unix-Pfade (<c>/srv/rollout</c>). Nicht erkannt wird
    /// <c>http(s)://</c>.
    ///
    /// <para>Der Unix-Zweig ist kein Schoenheitsfehler-Fix: DTM laeuft als
    /// AppImage auch unter Linux. Ohne ihn wuerde dort jeder lokale Ordner
    /// als Adresse behandelt und der Update-Check liefe still gegen GitHub —
    /// im CI genau so passiert, wo die Tests mit <c>/tmp/…</c> arbeiten und
    /// echte GitHub-Ergebnisse zurueckbekamen.</para>
    /// </summary>
    public static bool LooksLikeFolder(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return false;
        string s = channel.Trim();
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;
        // "/" deckt sowohl den Unix-Pfad als auch die "//server/share"-
        // Schreibweise ab; das Schema ist oben bereits ausgeschlossen.
        return s.StartsWith(@"\", StringComparison.Ordinal)
            || s.StartsWith("/", StringComparison.Ordinal)
            || (s.Length > 2 && s[1] == ':');
    }

    /// <summary>
    /// Der effektiv geltende Kanal: was in den Einstellungen steht, sonst
    /// <see cref="DefaultFolder"/>.
    /// </summary>
    public static string Resolve(string? fromSettings) =>
        string.IsNullOrWhiteSpace(fromSettings) ? DefaultFolder : fromSettings.Trim();

    /// <summary>Version aus einem Dateinamen lesen; <c>null</c> wenn keine drin steht.</summary>
    public static Version? ParseVersionFromFileName(string fileName)
    {
        Match m = VersionInName.Match(fileName);
        return m.Success && Version.TryParse(m.Groups[1].Value, out Version? v) ? v : null;
    }

    /// <summary>
    /// Auf vier Segmente bringen. Ohne das gilt "2.3.11" (Revision -1) als
    /// kleiner als "2.3.11.0" und ein Gleichstand wuerde als Update angeboten.
    /// </summary>
    public static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>
    /// Dateimuster des plattformpassenden Pakets. Windows bekommt das ZIP,
    /// Linux bevorzugt das AppImage (ersetzt sich selbst) und faellt sonst auf
    /// das tar.gz zurueck.
    /// </summary>
    public static IReadOnlyList<string> PackagePatterns(bool isWindows) =>
        isWindows ? ["DTM-*.zip"] : ["DTM-*.AppImage", "DTM-*.tar.gz"];

    /// <summary>
    /// Neuestes Paket im Ordner, nach <b>Version</b> sortiert — nicht nach
    /// Zeitstempel. Kopiert jemand ein aelteres Paket zurueck in den Ordner,
    /// ist es die juengste Datei und wuerde sonst als "Update" auf eine
    /// aeltere Version angeboten.
    ///
    /// <para>Die Reihenfolge der Muster entscheidet bei Gleichstand: unter
    /// Linux gewinnt das AppImage gegen das tar.gz derselben Version.</para>
    /// </summary>
    public static (string? Path, Version? Version) FindNewestPackage(string folder, bool isWindows)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                // Absichtlich Debug: ein Notebook ohne Netzlaufwerk ist der
                // Normalfall, nicht die Stoerung. Sonst steht im Log jedes
                // mobilen Nutzers taeglich ein Fehler, den niemand beheben kann.
                _logger.Debug("Update-Ordner {0} nicht erreichbar.", folder);
                return (null, null);
            }

            var candidates = new List<(string Path, Version Version, int Rank)>();
            IReadOnlyList<string> patterns = PackagePatterns(isWindows);

            for (int rank = 0; rank < patterns.Count; rank++)
            {
                foreach (string path in Directory.EnumerateFiles(folder, patterns[rank]))
                {
                    if (ParseVersionFromFileName(Path.GetFileName(path)) is { } v)
                        candidates.Add((path, v, rank));
                }
            }

            if (candidates.Count == 0)
            {
                _logger.Debug("Kein passendes Paket in {0}.", folder);
                return (null, null);
            }

            var best = candidates
                .OrderByDescending(c => Normalize(c.Version))
                .ThenBy(c => c.Rank)
                .First();

            return (best.Path, best.Version);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Suche im Update-Ordner {0} fehlgeschlagen.", folder);
            return (null, null);
        }
    }
}
