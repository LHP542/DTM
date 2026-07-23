namespace DTM.Config;

/// <summary>
/// Konfiguration für das FOC-SQL-PowerShell-Modul.
/// Wird in %APPDATA%\DTM\settings.json gespeichert und über
/// <see cref="AppSettingsStore"/> gelesen/geschrieben.
/// </summary>
public sealed class FocSqlConfig
{
    /// <summary>
    /// Voller Pfad zu einer FOC-SQL.psm1 (Override). Leer = Samba-Logik aktiv.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// UNC-Glob, von dem das FOC-SQL-Modul in den User-PSModulePath kopiert wird.
    /// Leer = eingebauter Default-Glob in FocSqlRuntime.
    /// </summary>
    public string SambaSource { get; set; } = string.Empty;

    /// <summary>
    /// Legacy (bis v2.2.0): UNC-Pfad zum Samba-Update-Verzeichnis.
    /// Seit v2.3.0 wird ueber GitHub Releases geupdatet (siehe
    /// <see cref="DTM.Updater.UpdateService"/>) — das Feld bleibt nur,
    /// damit bestehende settings.json weiter round-trippen; wird
    /// nirgends mehr gelesen.
    /// </summary>
    public string UpdateSource { get; set; } = string.Empty;
}
