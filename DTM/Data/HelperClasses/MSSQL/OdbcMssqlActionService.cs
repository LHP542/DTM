using DTM.MSSQL;
using NLog;

namespace DTM.Data.Mssql;

/// <summary>
/// Phase 10: T-SQL-Actions fuer OdbcDirect-Backend. Wickelt alles ab, was
/// FOC-SQL sonst ueber WinRM + MSSQL-Modul macht — nur eben direkt ueber
/// die ODBC/1433-Verbindung (siehe <see cref="MSSQL_ODBC.ExecuteNonQueryAsync"/>).
///
/// Design-Prinzipien:
/// - SQL-Injection: <c>QUOTENAME(@dbname)</c> serverseitig fuer Object-Namen,
///   ODBC-Positional-Params ('?') fuer Werte. Kein String-Concat auf Client.
/// - Live-Output: Actions reichen einen <c>Action&lt;string&gt; onInfo</c>-
///   Callback an <see cref="MSSQL_ODBC"/> durch, der PRINT/RAISERROR/DBCC-
///   Meldungen als Notices in den pwsh-Tab injiziert (siehe 10.4).
/// - Async: alle Methoden sind <c>Task</c>-basiert und blocken den UI-Thread
///   nicht. Serialisierung pro OdbcConnection uebernimmt der ActionLock in
///   MSSQL_ODBC.
/// </summary>
public sealed class OdbcMssqlActionService(MSSQL_ODBC odbc)
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private readonly MSSQL_ODBC _odbc = odbc;

    private static readonly HashSet<string> _recoveryModes = new(StringComparer.OrdinalIgnoreCase)
    { "FULL", "SIMPLE", "BULK_LOGGED" };

    private static readonly HashSet<string> _pageVerifyModes = new(StringComparer.OrdinalIgnoreCase)
    { "CHECKSUM", "TORN_PAGE_DETECTION", "NONE" };

    /// <summary>Whitelist-Check fuer Recovery-Mode (Unit-Test-Hook).</summary>
    public static bool IsValidRecoveryMode(string mode) => _recoveryModes.Contains(mode);

    /// <summary>Whitelist-Check fuer Page-Verify (Unit-Test-Hook).</summary>
    public static bool IsValidPageVerify(string pv) => _pageVerifyModes.Contains(pv);

    /// <summary>
    /// <c>ALTER DATABASE X SET RECOVERY (FULL|SIMPLE|BULK_LOGGED)</c>.
    /// Wechsel zu SIMPLE bricht die Log-Chain — verantwortet der Aufrufer
    /// (Confirm-Dialog in DTM).
    /// </summary>
    public Task SetRecoveryModeAsync(
        string database, string recovery,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        if (!_recoveryModes.Contains(recovery))
            throw new ArgumentException($"Ungueltiger Recovery-Modus: '{recovery}'", nameof(recovery));

        _logger.Info("OdbcDirect: SET RECOVERY {0} auf '{1}'", recovery.ToUpperInvariant(), database);

        // Recovery-Mode-Wert ist nicht parametrisierbar (Keyword), aber wir
        // haben ihn oben gegen die Whitelist geprueft — sicher fuer Concat.
        string sql =
            "DECLARE @db NVARCHAR(128) = ?;" +
            $"DECLARE @sql NVARCHAR(MAX) = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET RECOVERY {recovery.ToUpperInvariant()}';" +
            "EXEC (@sql);";

        return _odbc.ExecuteNonQueryAsync(sql, new object?[] { database }, onInfo, ct);
    }

    /// <summary>
    /// <c>ALTER DATABASE X SET QUERY_STORE = (ON|OFF)</c>.
    /// </summary>
    public Task SetQueryStoreAsync(
        string database, bool on,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        _logger.Info("OdbcDirect: SET QUERY_STORE = {0} auf '{1}'", on ? "ON" : "OFF", database);

        string sql =
            "DECLARE @db NVARCHAR(128) = ?;" +
            $"DECLARE @sql NVARCHAR(MAX) = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET QUERY_STORE = {(on ? "ON" : "OFF")}';" +
            "EXEC (@sql);";

        return _odbc.ExecuteNonQueryAsync(sql, new object?[] { database }, onInfo, ct);
    }

    /// <summary>
    /// <c>ALTER DATABASE X SET PAGE_VERIFY (CHECKSUM|TORN_PAGE_DETECTION|NONE)</c>.
    /// </summary>
    public Task SetPageVerifyAsync(
        string database, string pageVerify,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        if (!_pageVerifyModes.Contains(pageVerify))
            throw new ArgumentException($"Ungueltiger Page-Verify-Modus: '{pageVerify}'", nameof(pageVerify));

        _logger.Info("OdbcDirect: SET PAGE_VERIFY {0} auf '{1}'", pageVerify.ToUpperInvariant(), database);

        string sql =
            "DECLARE @db NVARCHAR(128) = ?;" +
            $"DECLARE @sql NVARCHAR(MAX) = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET PAGE_VERIFY {pageVerify.ToUpperInvariant()}';" +
            "EXEC (@sql);";

        return _odbc.ExecuteNonQueryAsync(sql, new object?[] { database }, onInfo, ct);
    }

    /// <summary>
    /// Setzt <c>COMPATIBILITY_LEVEL</c> auf den Wert der master-DB (Reset-zu-
    /// Server-Default). Analog zu <c>Database-Set-Compatibility</c> im
    /// MSSQL-Modul.
    /// </summary>
    public Task ResetCompatibilityAsync(
        string database,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        _logger.Info("OdbcDirect: SET COMPATIBILITY_LEVEL = master fuer '{0}'", database);

        // Master-Compatibility-Level einlesen und in dyn. SQL einbetten. Wert
        // ist eine Zahl (SMALLINT) — sicher fuer Concat, kommt aus master
        // und nicht vom User.
        string sql =
            "DECLARE @db NVARCHAR(128) = ?;" +
            "DECLARE @lvl INT = (SELECT compatibility_level FROM sys.databases WHERE database_id = 1);" +
            "DECLARE @sql NVARCHAR(MAX) = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET COMPATIBILITY_LEVEL = ' + CAST(@lvl AS NVARCHAR(10));" +
            "EXEC (@sql);";

        return _odbc.ExecuteNonQueryAsync(sql, new object?[] { database }, onInfo, ct);
    }

    /// <summary>
    /// MSSQL-Aequivalent zu <c>Set-Archive-Log</c>: togglet zwischen
    /// FULL (Log-Backups moeglich) und SIMPLE (kein Log-Backup). Wrapper
    /// um <see cref="SetRecoveryModeAsync"/> — echte MSSQL-Semantik. Oracle
    /// wird von diesem Service nicht behandelt.
    /// </summary>
    public Task SetArchiveLogAsync(
        string database, bool on,
        Action<string>? onInfo = null, CancellationToken ct = default)
        => SetRecoveryModeAsync(database, on ? "FULL" : "SIMPLE", onInfo, ct);

    // ------------------------------------------------------------------
    // Phase 10.3c: Snapshot Create / List / Restore / Drop
    // ------------------------------------------------------------------

    /// <summary>
    /// Baut den Snapshot-DB-Namen deterministisch aus DB + Timestamp.
    /// Format: <c>&lt;db&gt;_Snapshot_&lt;yyyyMMddHHmmss&gt;</c>. Kollisionsfrei
    /// durch Sekunden-Aufloesung, selbsterklaerend beim Restore-Auswahl-
    /// Dialog. Static gemacht, damit Tests das Muster verifizieren koennen.
    /// </summary>
    public static string BuildSnapshotName(string database, DateTime timestamp) =>
        $"{database}_Snapshot_{timestamp:yyyyMMddHHmmss}";

    /// <summary>
    /// Legt einen DB-Snapshot der angegebenen Datenbank an. Snapshot-DB-Name
    /// und -Filenames werden automatisch gebildet (siehe
    /// <see cref="BuildSnapshotName"/>). Multi-Data-File-DBs sind unterstuetzt:
    /// pro Original-Data-File eine <c>.ss</c>-Datei im selben Verzeichnis.
    /// Rueckgabe: Snapshot-Name (fuer spaeteren Restore/Drop-Aufruf).
    /// </summary>
    public async Task<string> CreateSnapshotAsync(
        string database,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        // 1) Data-File-Layout des Originals lesen. Bei Multi-Data-File-DBs
        //    liefert das mehrere Zeilen — alle muessen ins CREATE DATABASE-
        //    Statement, sonst weigert sich SQL Server. Log-Files (type=1)
        //    werden bewusst ausgeschlossen: Snapshots enthalten keinen Log.
        string filesSql =
            "SELECT name AS LogicalName, physical_name AS PhysicalName " +
            "FROM sys.master_files WHERE database_id = DB_ID(?) AND type = 0 " +
            "ORDER BY file_id;";

        var files = await _odbc.ExecuteReaderAsync<(string Logical, string Physical)>(
            filesSql,
            r => (r.GetString(0), r.GetString(1)),
            new object?[] { database }, ct).ConfigureAwait(false);

        if (files.Count == 0)
            throw new InvalidOperationException(
                $"CreateSnapshot: keine Data-Files fuer '{database}' gefunden (DB existiert nicht oder offline?).");

        string snapName = BuildSnapshotName(database, DateTime.Now);
        _logger.Info("OdbcDirect: CREATE SNAPSHOT '{0}' fuer '{1}' ({2} File(s))",
            snapName, database, files.Count);

        // 2) CREATE DATABASE snap ON (NAME = ..., FILENAME = '...') AS SNAPSHOT OF db.
        //    Filenames werden neben die Original-Data-Files gelegt:
        //    <dir>\<snapName>_<LogicalName>.ss. Kollisionsfrei durch
        //    Timestamp im snapName. LogicalName und Filename kommen aus
        //    sys.master_files (also von der SQL-Server-Registry, kein User-
        //    Input) — trotzdem defensive Escape fuer Single-Quotes im
        //    Filename-Literal.
        var fileClauses = new List<string>();
        foreach (var (logical, physical) in files)
        {
            string dir = Path.GetDirectoryName(physical) ?? string.Empty;
            string snapFile = Path.Combine(dir, $"{snapName}_{logical}.ss");
            string snapFileEsc = snapFile.Replace("'", "''");
            fileClauses.Add(
                $"(NAME = {QuoteName(logical)}, FILENAME = N'{snapFileEsc}')");
        }

        string createSql =
            "DECLARE @src NVARCHAR(128) = ?;" +
            $"DECLARE @sql NVARCHAR(MAX) = N'CREATE DATABASE {QuoteName(snapName)} ON " +
            string.Join(", ", fileClauses) +
            $" AS SNAPSHOT OF ' + QUOTENAME(@src);" +
            "EXEC (@sql);";

        await _odbc.ExecuteNonQueryAsync(createSql, new object?[] { database }, onInfo, ct)
                   .ConfigureAwait(false);

        return snapName;
    }

    /// <summary>
    /// Listet alle Snapshots der angegebenen Datenbank (sortiert nach
    /// Erstellungsdatum, neueste zuerst). Aus <c>sys.databases</c> ueber
    /// <c>source_database_id</c>.
    /// </summary>
    public Task<List<MssqlSnapshotInfo>> ListSnapshotsAsync(
        string database, CancellationToken ct = default)
    {
        string sql =
            "SELECT s.name, s.create_date " +
            "FROM sys.databases s " +
            "WHERE s.source_database_id = DB_ID(?) " +
            "ORDER BY s.create_date DESC;";

        return _odbc.ExecuteReaderAsync(
            sql,
            r => new MssqlSnapshotInfo(r.GetString(0), r.GetDateTime(1)),
            new object?[] { database }, ct);
    }

    /// <summary>
    /// <c>RESTORE DATABASE X FROM DATABASE_SNAPSHOT = Y</c>. Vor Restore
    /// wird die Ziel-DB kurz in SINGLE_USER geschaltet (rollback pending
    /// transactions), damit RESTORE nicht mit "database is in use"
    /// abbricht. TRY/CATCH stellt MULTI_USER auf jeden Fall wieder her,
    /// auch wenn RESTORE fehlschlaegt.
    /// </summary>
    public Task RestoreSnapshotAsync(
        string database, string snapshotName,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        _logger.Info("OdbcDirect: RESTORE '{0}' FROM DATABASE_SNAPSHOT = '{1}'",
            database, snapshotName);

        string sql =
            "DECLARE @db NVARCHAR(128) = ?;" +
            "DECLARE @snap NVARCHAR(128) = ?;" +
            "DECLARE @qDb NVARCHAR(258) = QUOTENAME(@db);" +
            "DECLARE @qSnap NVARCHAR(258) = QUOTENAME(@snap);" +
            "EXEC (N'ALTER DATABASE ' + @qDb + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE');" +
            "BEGIN TRY " +
            "  EXEC (N'RESTORE DATABASE ' + @qDb + N' FROM DATABASE_SNAPSHOT = ' + @qSnap);" +
            "  EXEC (N'ALTER DATABASE ' + @qDb + N' SET MULTI_USER');" +
            "END TRY " +
            "BEGIN CATCH " +
            "  EXEC (N'ALTER DATABASE ' + @qDb + N' SET MULTI_USER');" +
            "  THROW;" +
            "END CATCH;";

        return _odbc.ExecuteNonQueryAsync(sql, new object?[] { database, snapshotName }, onInfo, ct);
    }

    /// <summary>
    /// <c>DROP DATABASE snap</c>. Entfernt den Snapshot inkl. Files.
    /// </summary>
    public Task DropSnapshotAsync(
        string snapshotName,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        _logger.Info("OdbcDirect: DROP SNAPSHOT '{0}'", snapshotName);

        string sql =
            "DECLARE @snap NVARCHAR(128) = ?;" +
            "EXEC (N'DROP DATABASE ' + QUOTENAME(@snap));";

        return _odbc.ExecuteNonQueryAsync(sql, new object?[] { snapshotName }, onInfo, ct);
    }

    // C#-seitige QUOTENAME-Reproduktion fuer Statement-Teile, die als
    // Literal in dyn. SQL gehen (nicht ueber sp_executesql lauffaehig, weil
    // FILENAME nicht parametrisierbar ist). Reine 1:1-Kopie der T-SQL-
    // Semantik: eckige Klammern, ']' → ']]' escape.
    private static string QuoteName(string identifier) =>
        "[" + identifier.Replace("]", "]]") + "]";

    // ------------------------------------------------------------------
    // Phase 10.3d: Backup + Restore + Backup-Browser
    // ------------------------------------------------------------------

    /// <summary>
    /// Baut den Backup-Filepath deterministisch aus BackupRoot, DB und
    /// Timestamp. Flaches Layout (Lars-Entscheidung Phase 10.3d):
    /// <c>&lt;root&gt;\&lt;db&gt;\&lt;db&gt;-&lt;yyyyMMdd_HHmm&gt;.bak</c> —
    /// bewusst OHNE FOC-SQL-Unterordner '01 Taeglich', weil in der DMZ die
    /// FOC-SQL-Retention-Rotation nicht laeuft.
    /// </summary>
    public static string BuildBackupPath(string backupRoot, string database, DateTime timestamp) =>
        Path.Combine(
            backupRoot.TrimEnd('\\', '/'),
            database,
            $"{database}-{timestamp:yyyyMMdd_HHmm}.bak");

    /// <summary>
    /// Liest den SQL-Server-Default-BackupPath aus der Server-Registry
    /// (<c>xp_instance_regread</c>). Kein Extra-Config-Feld in DTM noetig.
    /// Kann leer sein, wenn der Server die Registry-Property nicht gesetzt
    /// hat — dann wirft die Methode InvalidOperationException.
    /// </summary>
    public async Task<string> GetBackupRootAsync(CancellationToken ct = default)
    {
        string sql =
            "DECLARE @root NVARCHAR(500);" +
            "EXEC master.dbo.xp_instance_regread " +
            "  N'HKEY_LOCAL_MACHINE'," +
            "  N'Software\\Microsoft\\MSSQLServer\\MSSQLServer'," +
            "  N'BackupDirectory'," +
            "  @root OUTPUT;" +
            "SELECT @root AS BackupRoot;";

        var rows = await _odbc.ExecuteReaderAsync(sql, r => r.IsDBNull(0) ? string.Empty : r.GetString(0),
                                                   null, ct).ConfigureAwait(false);
        string root = rows.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException(
                "SQL-Server-Default-BackupPath ist leer (xp_instance_regread).");
        return root;
    }

    /// <summary>
    /// Legt ein Full-Backup der Datenbank an. Ziel-Datei nach flachem
    /// Layout (siehe <see cref="BuildBackupPath"/>), Ziel-Verzeichnis wird
    /// per <c>xp_create_subdir</c> idempotent angelegt. WITH INIT, FORMAT,
    /// STATS = 10 → 10%-Progress-Meldungen kommen live via
    /// <paramref name="onInfo"/>. Rueckgabe: der Backup-Filepath (fuer
    /// UI-Feedback / Log).
    /// </summary>
    public async Task<string> BackupAsync(
        string database,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        string root = await GetBackupRootAsync(ct).ConfigureAwait(false);
        var timestamp = DateTime.Now;
        string file = BuildBackupPath(root, database, timestamp);
        string dir = Path.GetDirectoryName(file) ?? root;

        _logger.Info("OdbcDirect: BACKUP DATABASE '{0}' TO DISK = '{1}'", database, file);

        string sql =
            "DECLARE @db NVARCHAR(128) = ?;" +
            "DECLARE @dir NVARCHAR(500) = ?;" +
            "DECLARE @file NVARCHAR(500) = ?;" +
            "EXEC master.dbo.xp_create_subdir @dir;" +
            "DECLARE @sql NVARCHAR(MAX) = " +
            "  N'BACKUP DATABASE ' + QUOTENAME(@db) + " +
            "  N' TO DISK = @f WITH INIT, FORMAT, STATS = 10';" +
            "EXEC sp_executesql @sql, N'@f NVARCHAR(500)', @f = @file;";

        await _odbc.ExecuteNonQueryAsync(sql, new object?[] { database, dir, file }, onInfo, ct)
                   .ConfigureAwait(false);

        return file;
    }

    /// <summary>
    /// Listet die letzten 100 Full-Backups der DB aus <c>msdb.dbo.backupset</c>
    /// (Lars-Entscheidung: nur Fulls, konsistent zu <c>Get-DbBackups-MSSQL</c>
    /// im FOC-SQL-Weg). Sortiert nach <c>backup_finish_date</c> absteigend.
    /// </summary>
    public Task<List<MssqlBackupInfo>> ListBackupsAsync(
        string database, CancellationToken ct = default)
    {
        string sql =
            "SELECT TOP 100 " +
            "  s.database_name, s.backup_finish_date, s.backup_size, mf.physical_device_name " +
            "FROM msdb.dbo.backupset s " +
            "JOIN msdb.dbo.backupmediafamily mf ON s.media_set_id = mf.media_set_id " +
            "WHERE s.database_name = ? AND s.type = 'D' " +
            "ORDER BY s.backup_finish_date DESC;";

        return _odbc.ExecuteReaderAsync(
            sql,
            r => new MssqlBackupInfo(
                r.GetString(0),
                r.GetDateTime(1),
                r.IsDBNull(2) ? 0L : Convert.ToInt64(r.GetValue(2)),
                r.IsDBNull(3) ? string.Empty : r.GetString(3)),
            new object?[] { database }, ct);
    }

    /// <summary>
    /// Restore aus Backup-Datei. Vor RESTORE wird die Ziel-DB in
    /// SINGLE_USER geschaltet (rollback pending transactions), sonst
    /// bricht RESTORE mit "database is in use" ab. TRY/CATCH stellt
    /// MULTI_USER auf jeden Fall wieder her. WITH REPLACE, STATS = 10 →
    /// Fortschritt kommt via <paramref name="onInfo"/>.
    /// </summary>
    public Task RestoreBackupAsync(
        string database, string backupFile,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(backupFile))
            throw new ArgumentException("BackupFile darf nicht leer sein.", nameof(backupFile));

        _logger.Info("OdbcDirect: RESTORE DATABASE '{0}' FROM DISK = '{1}'", database, backupFile);

        string sql =
            "DECLARE @db NVARCHAR(128) = ?;" +
            "DECLARE @file NVARCHAR(500) = ?;" +
            "DECLARE @qDb NVARCHAR(258) = QUOTENAME(@db);" +
            "EXEC (N'ALTER DATABASE ' + @qDb + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE');" +
            "BEGIN TRY " +
            "  DECLARE @sql NVARCHAR(MAX) = " +
            "    N'RESTORE DATABASE ' + @qDb + N' FROM DISK = @f WITH REPLACE, STATS = 10';" +
            "  EXEC sp_executesql @sql, N'@f NVARCHAR(500)', @f = @file;" +
            "  EXEC (N'ALTER DATABASE ' + @qDb + N' SET MULTI_USER');" +
            "END TRY " +
            "BEGIN CATCH " +
            "  EXEC (N'ALTER DATABASE ' + @qDb + N' SET MULTI_USER');" +
            "  THROW;" +
            "END CATCH;";

        return _odbc.ExecuteNonQueryAsync(sql, new object?[] { database, backupFile }, onInfo, ct);
    }

    // ------------------------------------------------------------------
    // Phase 10.3e: Sessions-Kill + CHECKDB + Index-Rebuild + Shrink-Log
    // ------------------------------------------------------------------

    /// <summary>
    /// Beendet alle User-Sessions zur angegebenen Datenbank (analog zu
    /// <c>Close-DbSessions-MSSQL</c>). Der eigene Session-Kontext
    /// (@@SPID) wird ausgeschlossen — sonst killt sich der Aufruf selbst.
    /// </summary>
    public Task KillUserSessionsAsync(
        string database,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        _logger.Info("OdbcDirect: KILL user sessions fuer '{0}'", database);

        string sql =
            "DECLARE @db NVARCHAR(128) = ?;" +
            "DECLARE @sql NVARCHAR(MAX) = N'';" +
            "SELECT @sql = @sql + N'KILL ' + CAST(session_id AS NVARCHAR(10)) + N';' " +
            "FROM sys.dm_exec_sessions " +
            "WHERE database_id = DB_ID(@db) AND session_id <> @@SPID AND is_user_process = 1;" +
            "IF LEN(@sql) > 0 EXEC (@sql);";

        return _odbc.ExecuteNonQueryAsync(sql, new object?[] { database }, onInfo, ct);
    }

    /// <summary>
    /// <c>DBCC CHECKDB(&lt;db&gt;) WITH ALL_ERRORMSGS</c>. Alle DBCC-Meldungen
    /// (auch die grossen Table-Summaries) kommen live per
    /// <paramref name="onInfo"/>-Callback — der User sieht im pwsh-Tab
    /// exakt was der Server ausgibt.
    /// </summary>
    public Task CheckDbAsync(
        string database,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        _logger.Info("OdbcDirect: DBCC CHECKDB fuer '{0}'", database);

        string sql =
            "DECLARE @db NVARCHAR(128) = ?;" +
            "DECLARE @sql NVARCHAR(MAX) = N'DBCC CHECKDB (' + QUOTENAME(@db) + N') WITH ALL_ERRORMSGS';" +
            "EXEC (@sql);";

        return _odbc.ExecuteNonQueryAsync(sql, new object?[] { database }, onInfo, ct);
    }

    /// <summary>
    /// Rebuild aller Indizes auf User-Tables der DB. Statt eines langen
    /// dyn.-SQL-Cursors (der schwer zu debuggen ist) baut DTM die Table-
    /// Liste selbst und feuert pro Table einen kurzen ALTER-INDEX-Aufruf.
    /// Vorteil: Live-Progress per <paramref name="onInfo"/>-Notice fuer
    /// jede Table, klare Log-Zuordnung im pwsh-Tab.
    /// </summary>
    public async Task IndexRebuildAsync(
        string database,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        // Tables der Ziel-DB via QUERY auf sys.tables mit expliziter DB-
        // Referenz. Zwei-Teil-Namen: <schema>.<table>.
        string listSql =
            "DECLARE @db NVARCHAR(128) = ?;" +
            "DECLARE @sql NVARCHAR(MAX) = " +
            "  N'SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName " +
            "     FROM ' + QUOTENAME(@db) + N'.sys.tables t WHERE t.is_ms_shipped = 0 " +
            "     ORDER BY SCHEMA_NAME(t.schema_id), t.name';" +
            "EXEC (@sql);";

        var tables = await _odbc.ExecuteReaderAsync<(string Schema, string Table)>(
            listSql,
            r => (r.GetString(0), r.GetString(1)),
            new object?[] { database }, ct).ConfigureAwait(false);

        _logger.Info("OdbcDirect: Index-Rebuild auf '{0}' — {1} Table(s)", database, tables.Count);
        onInfo?.Invoke($"[Index-Rebuild: {tables.Count} Table(s) in '{database}']");

        foreach (var (schema, table) in tables)
        {
            ct.ThrowIfCancellationRequested();
            string qName = $"{QuoteName(schema)}.{QuoteName(table)}";
            onInfo?.Invoke($"  Rebuilding indexes on {qName} …");

            string sql =
                "DECLARE @db NVARCHAR(128) = ?;" +
                $"DECLARE @sql NVARCHAR(MAX) = N'USE ' + QUOTENAME(@db) + N'; ALTER INDEX ALL ON {qName} REBUILD';" +
                "EXEC (@sql);";

            await _odbc.ExecuteNonQueryAsync(sql, new object?[] { database }, onInfo, ct)
                       .ConfigureAwait(false);
        }

        onInfo?.Invoke($"[Index-Rebuild fertig fuer '{database}']");
    }

    /// <summary>
    /// Schrumpft alle Log-Files der DB auf 100 MB. Multi-Log-File-Setups
    /// (selten) werden korrekt behandelt. Analog zu
    /// <c>Database-Shrink-Log-File</c> im MSSQL-Modul.
    /// </summary>
    public async Task ShrinkLogAsync(
        string database,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        string logFilesSql =
            "SELECT name FROM sys.master_files WHERE database_id = DB_ID(?) AND type = 1;";

        var logFiles = await _odbc.ExecuteReaderAsync<string>(
            logFilesSql, r => r.GetString(0),
            new object?[] { database }, ct).ConfigureAwait(false);

        if (logFiles.Count == 0)
            throw new InvalidOperationException(
                $"ShrinkLog: keine Log-Datei fuer '{database}' gefunden.");

        _logger.Info("OdbcDirect: SHRINK LOG fuer '{0}' ({1} Log-File(s))", database, logFiles.Count);

        foreach (string logFile in logFiles)
        {
            ct.ThrowIfCancellationRequested();
            onInfo?.Invoke($"  DBCC SHRINKFILE({QuoteName(logFile)}, 100) in '{database}' …");

            string sql =
                "DECLARE @db NVARCHAR(128) = ?;" +
                $"DECLARE @sql NVARCHAR(MAX) = N'USE ' + QUOTENAME(@db) + N'; DBCC SHRINKFILE ({QuoteName(logFile)}, 100)';" +
                "EXEC (@sql);";

            await _odbc.ExecuteNonQueryAsync(sql, new object?[] { database }, onInfo, ct)
                       .ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------
    // Phase 10.3f: Cluster Health
    // ------------------------------------------------------------------

    /// <summary>
    /// Always-On-Cluster-Status aus <c>sys.availability_groups</c> +
    /// <c>sys.availability_replicas</c> + <c>sys.dm_hadr_availability_replica_states</c>.
    /// Output geht als Text-Notice pro Zeile in den pwsh-Tab (analog zum
    /// FOC-SQL-Get-ClusterHealthStatus-Output). Wenn kein Always-On
    /// konfiguriert ist, eine erklaerende Notice statt einer leeren
    /// Ergebnismenge. Read-only, harmlos.
    /// </summary>
    public async Task GetClusterHealthAsync(
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        _logger.Info("OdbcDirect: Get-ClusterHealthStatus");

        string sql =
            "SELECT ag.name, ar.replica_server_name, ars.role_desc, " +
            "       ars.operational_state_desc, ars.synchronization_health_desc, " +
            "       ars.connected_state_desc " +
            "FROM sys.availability_groups ag " +
            "JOIN sys.availability_replicas ar ON ag.group_id = ar.group_id " +
            "LEFT JOIN sys.dm_hadr_availability_replica_states ars ON ars.replica_id = ar.replica_id " +
            "ORDER BY ag.name, ar.replica_server_name;";

        var rows = await _odbc.ExecuteReaderAsync(
            sql,
            r => (
                Ag: r.IsDBNull(0) ? "?" : r.GetString(0),
                Replica: r.IsDBNull(1) ? "?" : r.GetString(1),
                Role: r.IsDBNull(2) ? "?" : r.GetString(2),
                Op: r.IsDBNull(3) ? "?" : r.GetString(3),
                Sync: r.IsDBNull(4) ? "?" : r.GetString(4),
                Conn: r.IsDBNull(5) ? "?" : r.GetString(5)
            ),
            null, ct).ConfigureAwait(false);

        if (rows.Count == 0)
        {
            onInfo?.Invoke("Kein Always-On-Cluster konfiguriert (keine Availability-Groups).");
            return;
        }

        onInfo?.Invoke($"[Cluster-Health: {rows.Count} Replica(s)]");
        foreach (var row in rows)
        {
            onInfo?.Invoke(
                $"  AG='{row.Ag}' Replica='{row.Replica}' Role={row.Role} " +
                $"Op={row.Op} Sync={row.Sync} Conn={row.Conn}");
        }
    }
}
