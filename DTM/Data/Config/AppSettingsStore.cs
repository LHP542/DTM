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
        catch (Exception ex)
        {
            _logger.Error(ex, "Fehler beim Laden der Einstellungen aus {0}", _path);
            return new FocSqlConfig();
        }
    }

    public static void SaveFocSql(FocSqlConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            SystemFile.WriteAllText(_path, json);
            _logger.Info("Einstellungen gespeichert nach {0}", _path);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Fehler beim Speichern der Einstellungen nach {0}", _path);
            throw;
        }
    }
}
