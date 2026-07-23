using NLog;

namespace DTM
{
    public interface IODBC_Factory
    {
        ODBC.IDTM_ODBC? Get_DATA(string Name, ServerCredential credential);
    }

    public class ODBC_Factory : IODBC_Factory
    {
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        // Cache-Key: "<Typ>::<Server>" case-insensitive. Mit Phase 6 (Multi-
        // Server-Support) koennen pro Typ mehrere Hosts existieren — ein
        // globaler Slot pro Typ (der frueher hier lag) hat jede zweite
        // MSSQL-/Oracle-Instanz auf den ersten Server umgebogen: Bug 2
        // "immer die gleichen DBs" und Bug 1 "keine Verbindung zum
        // zweiten Server" waren beide dieser eine Cache-Bug.
        private readonly Dictionary<string, ODBC.IDTM_ODBC> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        public ODBC.IDTM_ODBC? Get_DATA(string Name, ServerCredential credential)
        {
            string key = $"{Name}::{credential.Server}";
            if (_cache.TryGetValue(key, out var existing))
            {
                _logger.Debug("ODBC_Factory: Bestehende {0}-Instanz fuer '{1}' zurueckgegeben.", Name, credential.Server);
                return existing;
            }

            ODBC.IDTM_ODBC? instance = Name switch
            {
                "MSSQL"  => new MSSQL.MSSQL_ODBC(credential),
                "ORACLE" => new ORACLE.ORACLE_ODBC(credential),
                _        => null
            };

            if (instance is null)
            {
                _logger.Warn("ODBC_Factory: Unbekannter Datenbanktyp '{0}'", Name);
                return null;
            }

            _cache[key] = instance;
            _logger.Debug("ODBC_Factory: Neue {0}-Instanz erstellt fuer Server '{1}'.", Name, credential.Server);
            return instance;
        }
    }
}
