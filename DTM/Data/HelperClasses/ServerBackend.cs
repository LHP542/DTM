namespace DTM;

/// <summary>
/// Phase 10: pro DB-Server konfigurierbarer Ausführungspfad für die
/// FOC-SQL-Actions (Backup/Restore/Snapshot/Wartung/…).
///
/// <see cref="FocSql"/> = Standard-Weg: DTM ruft die FOC-SQL-Cmdlets im
/// pwsh-Runspace auf, das Modul macht PowerShell-Remoting via WinRM zum
/// SQL-Server und lädt dort das MSSQL-Modul. Kommt mit Mail-Versand,
/// Cluster-Health-Aggregation, Samba-File-Copy etc. — die volle FOC-SQL-
/// Orchestrierung.
///
/// <see cref="OdbcDirect"/> = DMZ-Weg: DTM schickt die T-SQL-Statements
/// direkt über die bestehende ODBC-Verbindung (Port 1433) zum Server.
/// Kein WinRM, kein PS-Modul auf dem Server nötig. Trade-off: Copy-
/// Database-ToSamba + Sync-Database-ToTest sind nicht verfügbar (FS-
/// Operationen, kein SQL-Weg). Mail-Versand entfällt bewusst.
///
/// Default = <see cref="FocSql"/> (backward-compat für bestehende
/// Bestandssetups; connections.json ohne das Feld deserialisiert dorthin).
/// Oracle ignoriert das Feld — Oracle-Actions gehen weiterhin über
/// SSH-Keys, keine Alternative aktuell verfügbar.
/// </summary>
public enum ServerBackend
{
    FocSql = 0,
    OdbcDirect = 1
}
