using System.Text.Json;
using NLog;
using SystemFile = System.IO.File;

namespace DTM.Config;

public static class AppSettingsStore
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    internal static string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DTM", "settings.json");

    public static FocSqlConfig LoadFocSql()
    {
        _logger.Debug("Lade Einstellungen aus {0}", _path);
        if (!SystemFile.Exists(_path)) return new FocSqlConfig();
        try
        {
            string json = SystemFile.ReadAllText(_path);
            var result = JsonSerializer.Deserialize<FocSqlConfig>(json) ?? new FocSqlConfig();
            _logger.Info("Einstellungen geladen.");
            return result;
        }
        catch (JsonException ex)
        {
            // Kaputtes JSON: sichern statt beim naechsten Save zu ueberschreiben.
            _logger.Error(ex, "Einstellungen in {0} sind defekt.", _path);
            JsonFileStore.Quarantine(_path);
            return new FocSqlConfig();
        }
        catch (Exception ex)
        {
            // IO-Fehler: Inhalt ist in Ordnung, nur gerade nicht lesbar.
            _logger.Error(ex, "Fehler beim Laden der Einstellungen aus {0}", _path);
            return new FocSqlConfig();
        }
    }

    public static void SaveFocSql(FocSqlConfig config)
    {
        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            JsonFileStore.WriteAtomic(_path, json);
            _logger.Info("Einstellungen gespeichert nach {0}", _path);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Fehler beim Speichern der Einstellungen nach {0}", _path);
            throw;
        }
    }
}
