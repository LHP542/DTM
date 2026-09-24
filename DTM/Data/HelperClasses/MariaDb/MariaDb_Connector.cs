using System.Data;
using DTM.ODBC;
using MySqlConnector;
using NLog;

namespace DTM.MariaDb;

/// <summary>
/// Lesender Zugriff auf einen MariaDB-/MySQL-Server — Datenbankliste und
/// Kennzahlen. Gegenstück zu <see cref="DTM.MSSQL.MSSQL_ODBC"/>, aber ohne
/// FOC-SQL und ohne PowerShell: MariaDB wird immer direkt angesprochen.
///
/// <para><b>Warum MySqlConnector statt ODBC:</b> rein managed, es muss also
/// auf keinem Client ein Treiber installiert werden. DTM wird als ZIP
/// verteilt; ein zusätzlich zu pflegender ODBC-Treiber wäre bei jedem
/// Nutzer eine Hürde. Das Interface <see cref="IDTM_ODBC"/> schreibt trotz
/// seines Namens keine ODBC-Technik vor — es verlangt nur die beiden
/// Lesemethoden.</para>
///
/// <para>Die Verbindung wird offen gehalten und von der
/// <see cref="ODBC_Factory"/> pro Server gecacht (gleiches Muster wie MSSQL).
/// Ein <see cref="SemaphoreSlim"/> serialisiert die Zugriffe, weil eine
/// einzelne <see cref="MySqlConnection"/> nicht für parallele Kommandos
/// gedacht ist.</para>
/// </summary>
public sealed class MariaDb_Connector(ServerCredential credential) : IDisposable, IDTM_ODBC
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>Standard-Port; greift, wenn im Servernamen keiner steht.</summary>
    private const uint DefaultPort = 3306;

    /// <summary>
    /// Schemas, die der Server selbst mitbringt. Sie tauchen in der
    /// Datenbankliste nicht auf — es sind keine Nutzdaten, und ein
    /// versehentliches Backup oder eine Wartung darauf wäre bestenfalls
    /// sinnlos.
    /// </summary>
    private static readonly string[] SystemSchemas =
        ["information_schema", "performance_schema", "mysql", "sys"];

    private ServerCredential Credential { get; } = credential;
    private MySqlConnection? _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal string BuildConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(Credential.ConnectionString))
            return Credential.ConnectionString;

        // "host:3307" im Server-Feld erlauben — sonst müsste man für einen
        // abweichenden Port den ganzen ConnectionString von Hand schreiben.
        //
        // IPv6 braucht dabei die Klammer-Form "[fe80::1]:3307". Ohne diese
        // Unterscheidung würde bei einer blanken IPv6-Adresse alles hinter
        // dem letzten ":" als Port gelesen: aus "fe80::1" würde Host "fe80:"
        // auf Port 1 — die Verbindung liefe gegen den falschen Rechner,
        // statt klar zu scheitern.
        string host = Credential.Server.Trim();
        uint port = DefaultPort;

        int bracket = host.StartsWith('[') ? host.IndexOf("]:", StringComparison.Ordinal) : -1;
        if (bracket > 0)
        {
            if (TryPort(host[(bracket + 2)..], out uint bracketPort))
            {
                port = bracketPort;
                host = host[1..bracket];
            }
        }
        else if (host.Count(c => c == ':') == 1)
        {
            int colon = host.IndexOf(':');
            if (colon > 0 && TryPort(host[(colon + 1)..], out uint plainPort))
            {
                port = plainPort;
                host = host[..colon];
            }
        }

        static bool TryPort(string text, out uint value) =>
            uint.TryParse(text, out value) && value is > 0 and < 65536;

        MySqlConnectionStringBuilder builder = new()
        {
            Server = host,
            Port = port,
            UserID = Credential.User,
            Password = Credential.Password,
            // Kein Default-Schema: DTM wechselt über die Datenbankliste und
            // qualifiziert jede Abfrage selbst. Ein fest gesetztes Schema
            // würde die Verbindung scheitern lassen, sobald es fehlt.
            ConnectionTimeout = 15,
        };
        return builder.ConnectionString;
    }

    private MySqlConnection OpenConnection()
    {
        if (_connection is { State: ConnectionState.Open }) return _connection;

        _connection?.Dispose();
        string cs = BuildConnectionString();
        _connection = new MySqlConnection(cs);
        _connection.Open();
        _logger.Info("MariaDB verbunden: {0}", LogMask.MaskConnectionString(cs));
        return _connection;
    }

    /// <summary>
    /// Führt eine Abfrage aus und bildet jede Zeile über
    /// <paramref name="map"/> ab. Parameter werden immer gebunden, nie in den
    /// SQL-Text geschrieben.
    /// </summary>
    private List<T> Query<T>(string sql, Func<MySqlDataReader, T> map,
        params (string Name, object? Value)[] parameters)
    {
        _gate.Wait();
        try
        {
            MySqlConnection conn = OpenConnection();
            using MySqlCommand cmd = new(sql, conn);
            foreach ((string name, object? value) in parameters)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

            List<T> result = [];
            using MySqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read()) result.Add(map(reader));
            return result;
        }
        catch (Exception ex)
        {
            // Der ConnectionString enthält das Passwort — niemals roh ins Log.
            _logger.Error(ex, "MariaDB-Abfrage fehlgeschlagen auf '{0}'.", Credential.Server);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public List<Database_Info> get_Datenbank_Names()
    {
        // Platzhalterliste statt String-Verkettung, damit auch die
        // Systemschema-Namen gebunden werden.
        string placeholders = string.Join(",", SystemSchemas.Select((_, i) => $"@sys{i}"));
        var parameters = SystemSchemas
            .Select((s, i) => ($"@sys{i}", (object?)s))
            .ToArray();

        List<Database_Info> result = Query(
            $"""
             SELECT schema_name, default_character_set_name
             FROM information_schema.schemata
             WHERE schema_name NOT IN ({placeholders})
             ORDER BY schema_name
             """,
            r => new Database_Info
            {
                Name = r.GetString(0),
                // MariaDB kennt keine numerische Datenbank-ID; der Name ist
                // der Schlüssel.
                Id = r.GetString(0),
                FQDN = string.Empty,
                // Ein vorhandenes Schema ist erreichbar — ein Online/Offline
                // wie bei MSSQL gibt es hier nicht.
                Status = Database_Status.up,
            },
            parameters);

        _logger.Info("MariaDB: {0} Datenbanken geladen ({1}).", result.Count, Credential.Server);
        return result;
    }

    public Database_Stats GetDatabase_Stats(Database_Info database)
    {
        Database_Stats_MariaDb stats = new()
        {
            DatabaseTyp = "MariaDB",
            Server = Credential.Server,
            Name = database.Name,
            State = "ONLINE",
        };

        // --- Größe, Tabellenzahl, Engines -------------------------------
        var size = Query(
            """
            SELECT COUNT(*)                     AS TableCount,
                   COALESCE(SUM(data_length),0) AS DataBytes,
                   COALESCE(SUM(index_length),0) AS IndexBytes
            FROM information_schema.tables
            WHERE table_schema = @db AND table_type = 'BASE TABLE'
            """,
            r => (Count: r.GetInt32(0), Data: r.GetInt64(1), Index: r.GetInt64(2)),
            ("@db", database.Name)).FirstOrDefault();

        stats.TableCount = size.Count;
        stats.DataSizeMB = BytesToMb(size.Data);
        stats.IndexSizeMB = BytesToMb(size.Index);
        stats.TotalSizeMB = BytesToMb(size.Data + size.Index);

        // --- Zeichensatz und Sortierung des Schemas -----------------------
        var schema = Query(
            """
            SELECT default_character_set_name, default_collation_name
            FROM information_schema.schemata
            WHERE schema_name = @db
            """,
            r => (Charset: r.GetString(0), Collation: r.GetString(1)),
            ("@db", database.Name)).FirstOrDefault();

        stats.CharacterSet = schema.Charset;
        stats.Collation = schema.Collation;

        // --- Storage-Engines ----------------------------------------------
        List<string> engines = Query(
            """
            SELECT engine, COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = @db AND table_type = 'BASE TABLE' AND engine IS NOT NULL
            GROUP BY engine
            ORDER BY COUNT(*) DESC
            """,
            r => $"{r.GetString(0)} ({r.GetInt32(1)})",
            ("@db", database.Name));
        stats.Engines = engines.Count > 0 ? string.Join(", ", engines) : null;

        // --- Server-Version ------------------------------------------------
        stats.ServerVersion = Query("SELECT VERSION()", r => r.GetString(0)).FirstOrDefault();

        // --- Sessions ------------------------------------------------------
        stats.Sessions = GetSessions(database.Name);
        stats.ActiveConnections = stats.Sessions.Count;

        _logger.Info("MariaDB: Stats für '{0}' geladen ({1} Tabellen, {2:N1} MB).",
            database.Name, stats.TableCount, stats.TotalSizeMB);
        return stats;
    }

    /// <summary>
    /// Verbindungen auf dieses Schema. Die eigene Verbindung wird
    /// herausgefiltert — sie als „aktive Session" zu zählen wäre irreführend,
    /// und ein Kill würde DTM die Verbindung unter den Füßen wegziehen.
    /// </summary>
    internal List<Session> GetSessions(string schema) => Query(
        """
        SELECT id, user, host, command, state
        FROM information_schema.processlist
        WHERE db = @db AND id <> CONNECTION_ID()
        ORDER BY id
        """,
        r => new Session
        {
            Username = r.IsDBNull(1) ? null : r.GetString(1),
            Maschine = r.IsDBNull(2) ? null : r.GetString(2),
            // MariaDB liefert kein Programm wie MSSQL; die Prozess-ID ist die
            // Information, die man zum Beenden braucht.
            Program = $"ID {r.GetInt64(0)}",
            Status = r.IsDBNull(3)
                ? (r.IsDBNull(4) ? null : r.GetString(4))
                : r.GetString(3),
        },
        ("@db", schema));

    private static double BytesToMb(long bytes) => Math.Round(bytes / 1024d / 1024d, 2);

    /// <summary>
    /// Für den Action-Service: eine frisch geöffnete Verbindung. Bewusst
    /// nicht die gecachte Leseverbindung — schreibende Aktionen laufen lang
    /// (Backup, OPTIMIZE TABLE) und würden die Stats-Abfragen blockieren.
    /// </summary>
    internal MySqlConnection CreateSeparateConnection()
    {
        MySqlConnection conn = new(BuildConnectionString());
        conn.Open();
        return conn;
    }

    /// <summary>Server-Adresse ohne Port — für externe Werkzeuge wie mariadb-dump.</summary>
    internal (string Host, uint Port) HostAndPort()
    {
        MySqlConnectionStringBuilder b = new(BuildConnectionString());
        return (b.Server, b.Port == 0 ? DefaultPort : b.Port);
    }

    internal ServerCredential CredentialRef => Credential;

    /// <summary>
    /// Die Server-Version, oder <c>null</c>, wenn sie sich gerade nicht holen
    /// lässt. Für den Versionsvergleich mit dem Dump-Werkzeug.
    ///
    /// <para>Bewusst mit Fangnetz: die Prüfung ist eine Dreingabe vor der
    /// Sicherung. Scheitert sie — etwa weil der Server gerade nicht erreichbar
    /// ist — soll daran nicht die Sicherung scheitern, sondern nur der
    /// Vergleich ausfallen.</para>
    /// </summary>
    internal string? TryGetServerVersion()
    {
        try
        {
            return Query("SELECT VERSION()", r => r.GetString(0)).FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Server-Version für den Versionsvergleich nicht lesbar.");
            return null;
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        _gate.Dispose();
    }
}
