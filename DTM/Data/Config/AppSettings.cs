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

    /// <summary>
    /// Einstellungen der lokalen REST-API. Eigenes Unterobjekt, damit die
    /// settings.json lesbar bleibt und nicht alles flach im Wurzelobjekt
    /// haengt. Fehlt der Block in einer bestehenden Datei, greifen die
    /// Defaults (API aus).
    /// </summary>
    public ApiSettings Api { get; set; } = new();
}

/// <summary>
/// Konfiguration der eingebauten REST-API (siehe <c>Data/Api/</c>).
///
/// <para><b>Standard ist AUS.</b> DTM ist ein Datenbank-Administrationswerkzeug —
/// ein offener Steuerkanal ist hier deutlich heikler als bei einer
/// Endanwender-App. Die API muss bewusst eingeschaltet werden, bindet
/// ausschliesslich an Loopback und verlangt ein Bearer-Token.</para>
/// </summary>
public sealed class ApiSettings
{
    /// <summary>API beim Start hochfahren. Ohne <see cref="BearerToken"/>
    /// beantwortet sie jeden Request mit 403 — das ist Absicht.</summary>
    public bool Enabled { get; set; }

    /// <summary>Loopback-Port. Nur wirksam, wenn <see cref="Enabled"/>
    /// oder <c>--api-port</c> gesetzt ist.</summary>
    public int Port { get; set; } = 8765;

    /// <summary>Statisches Bearer-Token. Leer = API verweigert alles.</summary>
    public string BearerToken { get; set; } = string.Empty;

    /// <summary>
    /// Erlaubt der API, auch destruktive Commands auszuloesen (Restore,
    /// Snapshot-Drop, Shrink-Log, Sessions-Kill …). Default <c>false</c>:
    /// die API ist als Beobachtungs- und Navigationskanal gedacht, nicht
    /// als Fernbedienung fuer Aktionen, die Datenbanken veraendern.
    /// </summary>
    public bool AllowDestructive { get; set; }
}
