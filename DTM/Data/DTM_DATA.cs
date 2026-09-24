using NLog;

namespace DTM;

public class DTM_DATA : IDTM_DATA, IDisposable
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    // O(1)-Lookup per ServerIdentity; bewahrt zusätzlich die Insertion-Order
    // über die separate Liste, damit der Tree-Aufbau im UI eine stabile
    // Reihenfolge sieht (wichtig bei vielen Servern in derselben Gruppe).
    private readonly Dictionary<ServerIdentity, DB_SERVER> _byIdentity;
    private readonly IODBC_Factory _factory;

    public IReadOnlyList<DB_SERVER> Servers { get; }

    public DTM_DATA(IReadOnlyList<DB_SERVER> servers, IODBC_Factory factory)
    {
        ArgumentNullException.ThrowIfNull(servers);
        Servers = servers;
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _byIdentity = servers.ToDictionary(s => s.Identity);
    }

    public List<Database_Info> get_Database_Names(ServerIdentity identity)
    {
        _logger.Debug("get_Database_Names: {0}", identity);
        try
        {
            DB_SERVER server = ResolveServer(identity);
            var result = _factory
                .Get_DATA(server.Typ.ToString(), server.serverCredential!)!
                .get_Datenbank_Names();
            _logger.Info("get_Database_Names: {0} Datenbanken geladen ({1}).", result.Count, identity);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "get_Database_Names fehlgeschlagen: {0}", identity);
            throw;
        }
    }

    public Database_Stats get_Database_Stats(ServerIdentity identity, Database_Info database)
    {
        _logger.Debug("get_Database_Stats: {0}, Datenbank={1}", identity, database.Name);
        try
        {
            DB_SERVER server = ResolveServer(identity);
            var result = _factory
                .Get_DATA(server.Typ.ToString(), server.serverCredential!)!
                .GetDatabase_Stats(database);
            _logger.Info("get_Database_Stats: Stats für '{0}' geladen ({1}).", database.Name, identity);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "get_Database_Stats fehlgeschlagen: {0}, Datenbank={1}", identity, database.Name);
            throw;
        }
    }

    private DB_SERVER ResolveServer(ServerIdentity identity)
    {
        if (_byIdentity.TryGetValue(identity, out DB_SERVER? server))
            return server;
        throw new KeyNotFoundException(
            $"Kein registrierter Server mit Identität '{identity}'. "
            + "Prüfe ConnectionStore / DI-Setup.");
    }

    public DTM.Data.Mssql.OdbcMssqlActionService GetMssqlActions(ServerIdentity identity)
    {
        DB_SERVER server = ResolveServer(identity);
        if (server.Typ != DB_SERVER.ServerTyp.MSSQL)
            throw new InvalidOperationException(
                $"OdbcMssqlActionService nur für MSSQL verfügbar (Server '{identity}' ist {server.Typ}).");
        var odbc = _factory.Get_DATA("MSSQL", server.serverCredential!) as DTM.MSSQL.MSSQL_ODBC
                   ?? throw new InvalidOperationException(
                       $"Factory lieferte keine MSSQL_ODBC-Instanz für '{identity}'.");
        return new DTM.Data.Mssql.OdbcMssqlActionService(odbc);
    }

    public DTM.Data.MariaDb.MariaDbActionService GetMariaDbActions(ServerIdentity identity) =>
        new(ResolveMariaDb(identity));

    public DTM.Data.MariaDb.MariaDbBackupService GetMariaDbBackups(ServerIdentity identity) =>
        new(ResolveMariaDb(identity), DTM.Config.AppSettingsStore.LoadFocSql().MariaDb);

    private DTM.MariaDb.MariaDb_Connector ResolveMariaDb(ServerIdentity identity)
    {
        DB_SERVER server = ResolveServer(identity);
        if (server.Typ != DB_SERVER.ServerTyp.MariaDB)
            throw new InvalidOperationException(
                $"MariaDB-Dienste nur für MariaDB verfügbar (Server '{identity}' ist {server.Typ}).");

        return _factory.Get_DATA("MariaDB", server.serverCredential!) as DTM.MariaDb.MariaDb_Connector
               ?? throw new InvalidOperationException(
                   $"Factory lieferte keinen MariaDb_Connector für '{identity}'.");
    }

    public DTM.Data.Olvm.OlvmSnapshotService GetOlvmSnapshotService(ServerIdentity identity)
    {
        DB_SERVER server = ResolveServer(identity);
        if (server.Typ != DB_SERVER.ServerTyp.ORACLE)
            throw new InvalidOperationException(
                $"OlvmSnapshotService nur für Oracle verfügbar (Server '{identity}' ist {server.Typ}).");
        // Frischer REST-Client pro Aufruf; der Service disposed ihn.
        // trustAllCertificates: true — gleiches Verhalten wie ORACLE_ODBC
        // (Self-signed OLVM-Zertifikate in typischen Setups).
        var rest = new DTM.ORACLE.REST(server.serverCredential!, trustAllCertificates: true);
        return new DTM.Data.Olvm.OlvmSnapshotService(rest);
    }

    /// <summary>
    /// Gibt die Verbindungen der Factory frei. Wird beim Austausch der
    /// Datenschicht gebraucht — der Verbindungsmanager baut beim Speichern
    /// eine neue, und ohne diesen Aufruf bliebe die alte samt allen offenen
    /// Server-Sitzungen liegen.
    /// </summary>
    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }
}
