using DTM.MariaDb;
using MySqlConnector;
using NLog;

namespace DTM.Data.MariaDb;

/// <summary>
/// Schreibende Aktionen auf einer MariaDB-Datenbank. Gegenstueck zu
/// <see cref="DTM.Data.Mssql.OdbcMssqlActionService"/>, aber mit dem, was
/// MariaDB tatsaechlich hergibt:
///
/// <list type="bullet">
/// <item><b>Sessions beenden</b> — <c>KILL</c> pro Verbindung.</item>
/// <item><b>Wartung</b> — <c>CHECK</c> / <c>OPTIMIZE</c> / <c>ANALYZE TABLE</c>
///       als Gegenstueck zu DBCC CHECKDB und Index-Rebuild.</item>
/// <item><b>Backup und Restore</b> — ueber die externen Werkzeuge
///       <c>mariadb-dump</c> und <c>mariadb</c>, siehe
///       <see cref="MariaDbBackupService"/>.</item>
/// </list>
///
/// <para><b>Was es bewusst nicht gibt:</b> Snapshots (kennt MariaDB nicht),
/// Recovery-Modus und Archive-Log (das Binlog ist serverweit, nicht pro
/// Datenbank) und Cluster-Health in der MSSQL-Form. Diese Aktions-Gruppen
/// bleiben in der UI ausgeblendet, statt Knoepfe anzubieten, die nichts
/// Sinnvolles tun koennen.</para>
///
/// <para><b>Bezeichner werden gequotet, nicht gebunden.</b> Tabellen- und
/// Datenbanknamen koennen in SQL nicht als Parameter uebergeben werden. Sie
/// gehen deshalb durch <see cref="QuoteIdentifier"/> — Backticks, mit
/// Verdopplung eines im Namen enthaltenen Backticks. Werte sind weiterhin
/// immer gebundene Parameter.</para>
/// </summary>
public sealed class MariaDbActionService(MariaDb_Connector connector)
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    private readonly MariaDb_Connector _connector = connector;

    /// <summary>Wartungsbefehle, die auf einzelne Tabellen wirken.</summary>
    public enum TableMaintenance
    {
        /// <summary>Prueft auf Fehler — lesend.</summary>
        Check,
        /// <summary>Raeumt auf und gibt Platz frei; schreibt die Tabelle neu.</summary>
        Optimize,
        /// <summary>Aktualisiert die Index-Statistiken fuer den Optimizer.</summary>
        Analyze,
    }

    /// <summary>
    /// Setzt einen Bezeichner in Backticks. Ein im Namen enthaltener Backtick
    /// wird verdoppelt — das ist die von MariaDB vorgesehene Escape-Regel und
    /// der einzige sichere Weg, weil Bezeichner nicht als Parameter gebunden
    /// werden koennen.
    /// </summary>
    public static string QuoteIdentifier(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return $"`{name.Replace("`", "``", StringComparison.Ordinal)}`";
    }

    /// <summary>
    /// Beendet alle fremden Verbindungen auf diese Datenbank. Die eigene
    /// Verbindung ist ausgenommen — sie zu beenden wuerde DTM den Boden
    /// unter den Fuessen wegziehen.
    /// </summary>
    public async Task<int> KillSessionsAsync(
        string database, Action<string>? onInfo = null, CancellationToken ct = default)
    {
        await using MySqlConnection conn = _connector.CreateSeparateConnection();

        List<long> ids = [];
        await using (MySqlCommand list = new(
            """
            SELECT id FROM information_schema.processlist
            WHERE db = @db AND id <> CONNECTION_ID()
            """, conn))
        {
            list.Parameters.AddWithValue("@db", database);
            await using MySqlDataReader reader = await list.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) ids.Add(reader.GetInt64(0));
        }

        if (ids.Count == 0)
        {
            onInfo?.Invoke("Keine fremden Verbindungen auf dieser Datenbank.");
            return 0;
        }

        int killed = 0;
        foreach (long id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // KILL nimmt keinen Parameter; die ID kommt aus der Abfrage
                // oben und ist ein long — kein Platz fuer Einschleusung.
                await using MySqlCommand kill = new($"KILL {id}", conn);
                await kill.ExecuteNonQueryAsync(ct);
                killed++;
                onInfo?.Invoke($"Verbindung {id} beendet.");
            }
            catch (MySqlException ex)
            {
                // Zwischen Auflisten und Beenden kann sich eine Verbindung
                // selbst verabschiedet haben — das ist kein Fehlschlag.
                onInfo?.Invoke($"Verbindung {id} war bereits weg ({ex.Message}).");
            }
        }

        _logger.Info("MariaDB: {0} von {1} Verbindungen auf '{2}' beendet.", killed, ids.Count, database);
        return killed;
    }

    /// <summary>
    /// Fuehrt einen Wartungsbefehl ueber alle Basistabellen der Datenbank aus,
    /// eine Tabelle nach der anderen. Der Fortschritt geht laufend an
    /// <paramref name="onInfo"/> — bei grossen Datenbanken laeuft das sonst
    /// minutenlang ohne jedes Lebenszeichen.
    /// </summary>
    public async Task RunTableMaintenanceAsync(
        string database, TableMaintenance operation,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        string verb = operation switch
        {
            TableMaintenance.Check => "CHECK TABLE",
            TableMaintenance.Optimize => "OPTIMIZE TABLE",
            TableMaintenance.Analyze => "ANALYZE TABLE",
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        await using MySqlConnection conn = _connector.CreateSeparateConnection();

        List<string> tables = [];
        await using (MySqlCommand list = new(
            """
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = @db AND table_type = 'BASE TABLE'
            ORDER BY table_name
            """, conn))
        {
            list.Parameters.AddWithValue("@db", database);
            await using MySqlDataReader reader = await list.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) tables.Add(reader.GetString(0));
        }

        if (tables.Count == 0)
        {
            onInfo?.Invoke($"'{database}' enthaelt keine Basistabellen — nichts zu tun.");
            return;
        }

        onInfo?.Invoke($"{verb} fuer {tables.Count} Tabellen in '{database}' …");
        string quotedDb = QuoteIdentifier(database);
        int index = 0;

        foreach (string table in tables)
        {
            ct.ThrowIfCancellationRequested();
            index++;

            await using MySqlCommand cmd = new(
                $"{verb} {quotedDb}.{QuoteIdentifier(table)}", conn);
            await using MySqlDataReader reader = await cmd.ExecuteReaderAsync(ct);

            // Die Wartungsbefehle liefern je Tabelle Zeilen mit
            // (Table, Op, Msg_type, Msg_text). Interessant ist die letzte
            // Meldung: "OK" im Normalfall, sonst der Grund.
            string? lastType = null;
            string? lastText = null;
            while (await reader.ReadAsync(ct))
            {
                lastType = reader.IsDBNull(2) ? null : reader.GetString(2);
                lastText = reader.IsDBNull(3) ? null : reader.GetString(3);
            }

            string summary = string.Equals(lastText, "OK", StringComparison.OrdinalIgnoreCase)
                ? "OK"
                : $"{lastType}: {lastText}";
            onInfo?.Invoke($"  [{index}/{tables.Count}] {table} — {summary}");
        }

        _logger.Info("MariaDB: {0} auf {1} Tabellen in '{2}' ausgefuehrt.", verb, tables.Count, database);
        onInfo?.Invoke($"{verb} abgeschlossen ({tables.Count} Tabellen).");
    }
}
