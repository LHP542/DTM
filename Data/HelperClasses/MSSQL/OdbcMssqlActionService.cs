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
}
