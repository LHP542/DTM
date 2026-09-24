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
    /// Quelle fuer Updates. Zwei Schreibweisen sind moeglich, der Typ wird
    /// daran erkannt (siehe <see cref="DTM.Updater.UpdateChannel"/>):
    /// <list type="bullet">
    /// <item>UNC-Pfad oder Laufwerk → Ordner im Netz (Regelweg im Firmennetz,
    ///       kein Proxy und kein Internetzugang noetig).</item>
    /// <item><c>https://…</c> → GitHub-Releases-API (Entwicklung ausserhalb
    ///       des Firmennetzes).</item>
    /// </list>
    ///
    /// <para>Leer = <see cref="DTM.Updater.UpdateChannel.DefaultFolder"/>.</para>
    ///
    /// <para><b>Warum ein neues Feld statt des alten <c>UpdateSource</c>:</b>
    /// Bis v2.2.0 gab es schon einmal einen Samba-Update-Weg, dessen Pfad in
    /// <c>UpdateSource</c> stand — bei Bestandsnutzern zeigt der auf das
    /// inzwischen abgeloeste Verzeichnis <c>…\MS-SQL\DTM_Update\AktuelleVersion</c>.
    /// Haette man das Feld wiederverwendet, wuerden genau die Nutzer mit
    /// Altbestand auf dem falschen Ordner landen, und zwar unbemerkt. Der neue
    /// Name faengt bei allen mit dem Default an; <c>UpdateSource</c> faellt
    /// beim naechsten Speichern aus der Datei (unbekannte Felder werden beim
    /// Lesen ignoriert).</para>
    /// </summary>
    public string UpdateChannel { get; set; } = string.Empty;

    /// <summary>
    /// Einstellungen der lokalen REST-API. Eigenes Unterobjekt, damit die
    /// settings.json lesbar bleibt und nicht alles flach im Wurzelobjekt
    /// haengt. Fehlt der Block in einer bestehenden Datei, greifen die
    /// Defaults (API aus).
    /// </summary>
    public ApiSettings Api { get; set; } = new();

    /// <summary>
    /// Einstellungen fuer die MariaDB-Werkzeuge. Fehlt der Block, greifen
    /// die Defaults.
    /// </summary>
    public MariaDbSettings MariaDb { get; set; } = new();
}

/// <summary>
/// Pfade fuer die externen MariaDB-Kommandozeilenwerkzeuge und das
/// Backup-Ziel.
///
/// <para><b>Warum externe Werkzeuge:</b> MariaDB kennt kein
/// <c>BACKUP DATABASE</c> wie MSSQL. Ein vollstaendiger, wieder einspielbarer
/// Dump entsteht nur ueber <c>mariadb-dump</c> (frueher <c>mysqldump</c>);
/// <c>SELECT … INTO OUTFILE</c> schreibt serverseitig und pro Tabelle und ist
/// kein Ersatz. DTM startet das Werkzeug deshalb lokal als Prozess.</para>
/// </summary>
public sealed class MariaDbSettings
{
    /// <summary>
    /// Voller Pfad zu <c>mariadb-dump</c>. Leer = in <c>PATH</c> suchen
    /// (<c>mariadb-dump</c>, dann <c>mysqldump</c>).
    /// </summary>
    public string DumpPath { get; set; } = string.Empty;

    /// <summary>
    /// Voller Pfad zum Client <c>mariadb</c> — fuer das Zurueckspielen eines
    /// Dumps. Leer = in <c>PATH</c> suchen (<c>mariadb</c>, dann <c>mysql</c>).
    /// </summary>
    public string ClientPath { get; set; } = string.Empty;

    /// <summary>
    /// Wurzelverzeichnis fuer Dumps. Leer =
    /// <c>%USERPROFILE%\DTM-Backups\MariaDB</c>. DTM legt darunter je Server
    /// und Datenbank einen Unterordner an.
    /// </summary>
    public string BackupRoot { get; set; } = string.Empty;
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
