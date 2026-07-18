using NLog;

namespace DTM
{
    public class DtmData : IDtmData
    {
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        // O(1)-Lookup per ServerIdentity; bewahrt zusaetzlich die Insertion-Order
        // ueber die separate Liste, damit der Tree-Aufbau im UI eine stabile
        // Reihenfolge sieht (wichtig bei vielen Servern in derselben Gruppe).
        private readonly Dictionary<ServerIdentity, DbServer> _byIdentity;
        private readonly IOdbcFactory _factory;

        public IReadOnlyList<DbServer> Servers { get; }

        public DtmData(IReadOnlyList<DbServer> servers, IOdbcFactory factory)
        {
            ArgumentNullException.ThrowIfNull(servers);
            Servers = servers;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _byIdentity = servers.ToDictionary(s => s.Identity);
        }

        public List<DatabaseInfo> get_Database_Names(ServerIdentity identity)
        {
            _logger.Debug("get_Database_Names: {0}", identity);
            try
            {
                DbServer server = ResolveServer(identity);
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

        public DatabaseStats get_Database_Stats(ServerIdentity identity, DatabaseInfo database)
        {
            _logger.Debug("get_Database_Stats: {0}, Datenbank={1}", identity, database.Name);
            try
            {
                DbServer server = ResolveServer(identity);
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

        private DbServer ResolveServer(ServerIdentity identity)
        {
            if (_byIdentity.TryGetValue(identity, out DbServer? server))
                return server;
            throw new KeyNotFoundException(
                $"Kein registrierter Server mit Identitaet '{identity}'. "
                + "Pruefe ConnectionStore / DI-Setup.");
        }

        public DTM.Data.Mssql.OdbcMssqlActionService GetMssqlActions(ServerIdentity identity)
        {
            DbServer server = ResolveServer(identity);
            if (server.Typ != DbServer.ServerTyp.MSSQL)
                throw new InvalidOperationException(
                    $"OdbcMssqlActionService nur fuer MSSQL verfuegbar (Server '{identity}' ist {server.Typ}).");
            var odbc = _factory.Get_DATA("MSSQL", server.serverCredential!) as DTM.MSSQL.MssqlOdbcClient
                       ?? throw new InvalidOperationException(
                           $"Factory lieferte keine MssqlOdbcClient-Instanz fuer '{identity}'.");
            return new DTM.Data.Mssql.OdbcMssqlActionService(odbc);
        }

        public DTM.Data.Olvm.OlvmSnapshotService GetOlvmSnapshotService(ServerIdentity identity)
        {
            DbServer server = ResolveServer(identity);
            if (server.Typ != DbServer.ServerTyp.ORACLE)
                throw new InvalidOperationException(
                    $"OlvmSnapshotService nur fuer Oracle verfuegbar (Server '{identity}' ist {server.Typ}).");
            // Frischer REST-Client pro Aufruf; der Service disposed ihn.
            // trustAllCertificates: true — gleiches Verhalten wie OracleOdbcClient
            // (Self-signed OLVM-Zertifikate in typischen Setups).
            var rest = new DTM.ORACLE.OracleRestClient(server.serverCredential!, trustAllCertificates: true);
            return new DTM.Data.Olvm.OlvmSnapshotService(rest);
        }
    }
}
