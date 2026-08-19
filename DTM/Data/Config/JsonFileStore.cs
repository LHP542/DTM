using NLog;
using SystemFile = System.IO.File;

namespace DTM.Config;

/// <summary>
/// Gemeinsame Datei-Primitiven fuer die JSON-Stores (<see cref="ConnectionStore"/>,
/// <see cref="AppSettingsStore"/>).
///
/// Zwei Regeln, die beide Stores vorher verletzt haben:
///
/// 1. <b>Atomar schreiben.</b> Ein <c>WriteAllText</c> direkt auf die Zieldatei
///    laesst bei Absturz/Stromausfall mitten im Schreiben eine halbe Datei
///    zurueck. Stattdessen erst nach <c>&lt;datei&gt;.tmp</c>, dann
///    <c>File.Move(tmp, ziel, overwrite: true)</c> — das Move ist atomar.
///
/// 2. <b>Defekte Daten nicht stillschweigend verlieren.</b> Laesst sich die
///    Datei nicht deserialisieren, wurde vorher einfach ein leeres Ergebnis
///    zurueckgegeben — der naechste Save hat die kaputte Datei dann endgueltig
///    ueberschrieben. Bei <c>connections.json</c> heisst das: alle Server samt
///    DPAPI-Passwoertern weg, ohne Kopie. Jetzt wandert die kaputte Datei nach
///    <c>&lt;datei&gt;.broken</c> und bleibt fuer Diagnose/Rettung erhalten.
///
/// Bewusst NICHT quarantaenisiert wird bei IO-Fehlern (Datei gesperrt, Netz-
/// laufwerk kurz weg): dort ist der Inhalt ja in Ordnung, nur gerade nicht
/// lesbar. Ein Verschieben wuerde intakte Daten aus dem Weg raeumen.
/// </summary>
internal static class JsonFileStore
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Schreibt <paramref name="json"/> atomar nach <paramref name="path"/>.
    /// Legt das Zielverzeichnis an, falls noetig.
    /// </summary>
    public static void WriteAtomic(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        try
        {
            SystemFile.WriteAllText(tmp, json);
            SystemFile.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Halb geschriebene .tmp nicht liegen lassen — sie wuerde beim
            // naechsten Versuch ohnehin ueberschrieben, aber ein Restmuell im
            // AppData-Ordner verwirrt bei der Fehlersuche.
            TryDeleteTemp(tmp);
            throw;
        }
    }

    /// <summary>
    /// Verschiebt eine nicht deserialisierbare Datei nach
    /// <c>&lt;datei&gt;.broken</c>. Schlaegt das fehl (z.B. Datei gesperrt),
    /// wird nur geloggt — der Aufrufer startet in jedem Fall leer weiter.
    /// </summary>
    public static void Quarantine(string path)
    {
        string broken = path + ".broken";
        try
        {
            SystemFile.Move(path, broken, overwrite: true);
            _logger.Error("Defekte Datei nach {0} gesichert. Es wird leer weitergestartet.", broken);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Defekte Datei {0} konnte nicht nach {1} gesichert werden.", path, broken);
        }
    }

    private static void TryDeleteTemp(string tmp)
    {
        try
        {
            if (SystemFile.Exists(tmp)) SystemFile.Delete(tmp);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Temporaere Datei {0} konnte nicht aufgeraeumt werden.", tmp);
        }
    }
}
