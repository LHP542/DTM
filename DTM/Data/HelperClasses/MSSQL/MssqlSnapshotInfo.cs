namespace DTM.Data.Mssql;

/// <summary>
/// Ein DB-Snapshot auf dem MSSQL-Server (aus <c>sys.databases</c> gelesen —
/// alle DBs mit gesetztem <c>source_database_id</c>). Wird vom
/// Restore-Snapshot-Auswahl-Dialog (im OdbcDirect-Modus) angezeigt und dem
/// User zur Auswahl vorgelegt.
/// </summary>
public sealed record MssqlSnapshotInfo(string Name, DateTime CreatedAt);
