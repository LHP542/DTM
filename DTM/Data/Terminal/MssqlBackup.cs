namespace DTM.Data.Terminal;

/// <summary>
/// Strukturierter Eintrag aus dem FOC-SQL-Befehl <c>Get-DbBackups</c>:
/// eine einzelne <c>.bak</c>-Datei im Backup-Verzeichnis einer MSSQL-DB.
/// Aktuell MSSQL-only (Oracle-Backups via RMAN noch nicht im Scope).
/// </summary>
public sealed record MssqlBackup(
    string Name,
    DateTime LastWriteTime,
    long SizeBytes,
    string Path)
{
    /// <summary>Human-readable Groesse (z. B. „2.4 GB", „542 MB").</summary>
    public string SizeDisplay => FormatSize(SizeBytes);

    private static string FormatSize(long bytes)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        const double gb = mb * 1024;
        return bytes switch
        {
            < (long)kb            => $"{bytes} B",
            < (long)mb            => $"{bytes / kb:0.0} KB",
            < (long)gb            => $"{bytes / mb:0.0} MB",
            _                     => $"{bytes / gb:0.00} GB",
        };
    }
}
