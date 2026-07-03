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
}
