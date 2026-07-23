namespace DTM.Data.Mssql;

/// <summary>
/// Ein Backup-Eintrag aus <c>msdb.dbo.backupset</c> (Server-verwaltete
/// History). Wird vom Backup-Browser im OdbcDirect-Modus angezeigt und
/// vom Restore-Aufruf als Quelle referenziert.
/// </summary>
public sealed record MssqlBackupInfo(
    string DatabaseName,
    DateTime FinishedAt,
    long SizeBytes,
    string Path);
