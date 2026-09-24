using NLog;
using SystemFile = System.IO.File;

namespace DTM.Updater;

/// <summary>
/// Notiz eines gescheiterten Update-Austauschs.
///
/// <para><b>Wozu:</b> Schlägt das Ersetzen der Dateien fehl, startet das
/// Austausch-Skript die <i>alte</i> Version wieder. Die prüft beim Start auf
/// Updates, findet dasselbe Paket, bietet es an, lädt es, beendet sich — und
/// scheitert erneut. Am 2026-09-24 ist DTM genau so elfmal im Kreis gelaufen,
/// ohne dass der Nutzer erfahren hätte, woran es liegt.</para>
///
/// <para>Der Marker durchbricht den Kreis: das Skript legt ihn bei einem
/// Fehlschlag an, und der automatische Update-Hinweis beim Start unterbleibt
/// dann einmal. Die manuelle Prüfung über das Über-Fenster bleibt erreichbar —
/// wer es noch einmal versuchen will, kann das jederzeit.</para>
///
/// <para>Er räumt sich selbst weg: sobald die laufende Version die damals
/// angestrebte erreicht hat, ist die Notiz gegenstandslos. Das Skript löscht
/// sie zusätzlich nach einer erfolgreichen Kopie.</para>
/// </summary>
public static class UpdateFailureMarker
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Liegt neben der übrigen Konfiguration, nicht im Programmverzeichnis:
    /// dorthin schreibt der Austausch selbst, und ein Marker, den die nächste
    /// Kopie überschreibt, hilft niemandem.
    /// </summary>
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DTM", "letztes-update-fehlgeschlagen.txt");

    /// <summary>
    /// Die Version, deren Austausch gescheitert ist — oder <c>null</c>, wenn es
    /// keinen Marker gibt bzw. sein Inhalt nicht lesbar ist.
    /// </summary>
    public static Version? Read()
    {
        try
        {
            if (!SystemFile.Exists(Path)) return null;
            string text = SystemFile.ReadAllText(Path).Trim();
            return Version.TryParse(text, out Version? v) ? v : null;
        }
        catch (Exception ex)
        {
            // Ein unlesbarer Marker darf den Start nicht aufhalten.
            _logger.Warn(ex, "Marker für das letzte Update nicht lesbar.");
            return null;
        }
    }

    /// <summary>Entfernt die Notiz. Fehler dabei sind unkritisch.</summary>
    public static void Clear()
    {
        try
        {
            if (SystemFile.Exists(Path)) SystemFile.Delete(Path);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Marker für das letzte Update liess sich nicht entfernen.");
        }
    }

    /// <summary>
    /// Entscheidet, ob der automatische Hinweis für <paramref name="angeboten"/>
    /// unterdrückt wird.
    ///
    /// <para>Rein und ohne Dateizugriff, damit die Regel testbar ist: der
    /// Hinweis unterbleibt genau dann, wenn schon einmal dieselbe oder eine
    /// neuere Version vergeblich versucht wurde und die laufende Version noch
    /// darunter liegt.</para>
    /// </summary>
    /// <param name="gescheitert">Inhalt des Markers, oder <c>null</c>.</param>
    /// <param name="laufend">Version, die gerade läuft.</param>
    /// <param name="angeboten">Version, die angeboten würde.</param>
    public static bool ShouldSuppress(Version? gescheitert, Version laufend, Version angeboten)
    {
        if (gescheitert is null) return false;

        // Der Austausch hat es doch noch geschafft (oder jemand hat von Hand
        // aktualisiert) — die Notiz ist gegenstandslos.
        if (laufend >= gescheitert) return false;

        // Inzwischen liegt eine NEUERE Version bereit als die, die gescheitert
        // ist. Die ist einen Versuch wert — vielleicht behebt gerade sie den
        // Fehler.
        return angeboten <= gescheitert;
    }
}
