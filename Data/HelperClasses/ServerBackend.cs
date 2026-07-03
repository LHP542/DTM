namespace DTM;

/// <summary>
/// Phase 10: pro DB-Server konfigurierbarer Ausfuehrungspfad fuer die
/// FOC-SQL-Actions (Backup/Restore/Snapshot/Wartung/…).
///
/// <see cref="FocSql"/> = Standard-Weg: DTM ruft die FOC-SQL-Cmdlets im
/// pwsh-Runspace auf, das Modul macht PowerShell-Remoting via WinRM zum
/// SQL-Server und laedt dort das MSSQL-Modul. Kommt mit Mail-Versand,
/// Cluster-Health-Aggregation, Samba-File-Copy etc. — die volle FOC-SQL-
/// Orchestrierung.
///
/// <see cref="OdbcDirect"/> = DMZ-Weg: DTM schickt die T-SQL-Statements
/// direkt ueber die bestehende ODBC-Verbindung (Port 1433) zum Server.
/// Kein WinRM, kein PS-Modul auf dem Server noetig. Trade-off: Copy-
/// Database-ToSamba + Sync-Database-ToTest sind nicht verfuegbar (FS-
/// Operationen, kein SQL-Weg). Mail-Versand entfaellt bewusst.
///
/// Default = <see cref="FocSql"/> (backward-compat fuer bestehende
/// Bestandssetups; connections.json ohne das Feld deserialisiert dorthin).
/// Oracle ignoriert das Feld — Oracle-Actions gehen weiterhin ueber
/// SSH-Keys, keine Alternative aktuell verfuegbar.
/// </summary>
public enum ServerBackend
{
    FocSql = 0,
    OdbcDirect = 1
}
