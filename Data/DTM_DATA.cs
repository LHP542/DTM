using NLog;

namespace DTM
{
    public class DTM_DATA : IDTM_DATA
    {
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        // O(1)-Lookup per ServerIdentity; bewahrt zusaetzlich die Insertion-Order
        // ueber die separate Liste, damit der Tree-Aufbau im UI eine stabile
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
                _logger.Info("get_Database_Stats: Stats fuer '{0}' geladen ({1}).", database.Name, identity);
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
                $"Kein registrierter Server mit Identitaet '{identity}'. "
                + "Pruefe ConnectionStore / DI-Setup.");
        }

        public DTM.Data.Mssql.OdbcMssqlActionService GetMssqlActions(ServerIdentity identity)
        {
            DB_SERVER server = ResolveServer(identity);
            if (server.Typ != DB_SERVER.ServerTyp.MSSQL)
                throw new InvalidOperationException(
                    $"OdbcMssqlActionService nur fuer MSSQL verfuegbar (Server '{identity}' ist {server.Typ}).");
            var odbc = _factory.Get_DATA("MSSQL", server.serverCredential!) as DTM.MSSQL.MSSQL_ODBC
                       ?? throw new InvalidOperationException(
                           $"Factory lieferte keine MSSQL_ODBC-Instanz fuer '{identity}'.");
            return new DTM.Data.Mssql.OdbcMssqlActionService(odbc);
        }
    }
}
