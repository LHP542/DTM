using NLog;

namespace DTM;

public interface IODBC_Factory
{
    public ODBC.IDTM_ODBC? Get_DATA(string Name, ServerCredential credential);
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

        // Gross-/Kleinschreibung vereinheitlichen: der Aufrufer reicht
        // ServerTyp.ToString() durch, und das liefert "MariaDB" — ein
        // case-sensitiver Vergleich gegen "MARIADB" ginge ins Leere.
        ODBC.IDTM_ODBC? instance = Name.ToUpperInvariant() switch
        {
            "MSSQL"   => new MSSQL.MSSQL_ODBC(credential),
            "ORACLE"  => new ORACLE.ORACLE_ODBC(credential),
            // MariaDB laeuft ueber MySqlConnector, nicht ueber ODBC — das
            // Interface verlangt nur die Lesemethoden, keine Technik.
            "MARIADB" => new MariaDb.MariaDb_Connector(credential),
            _         => null
        };

        if (instance is null)
        {
            // Frueher nur eine Warnung und null zurueck — die Aufrufer in
            // DTM_DATA dereferenzieren das Ergebnis aber mit "!", was zu
            // einer NullReferenceException ohne jeden Hinweis auf die
            // Ursache fuehrte. Eine klare Meldung ist hier mehr wert als
            // ein stiller Rueckgabewert.
            _logger.Error("ODBC_Factory: Kein Backend fuer Datenbanktyp '{0}'.", Name);
            throw new NotSupportedException(
                $"Fuer den Datenbanktyp '{Name}' gibt es kein Backend. "
                + "Unterstuetzt werden MSSQL, ORACLE und MARIADB.");
        }

        _cache[key] = instance;
        _logger.Debug("ODBC_Factory: Neue {0}-Instanz erstellt fuer Server '{1}'.", Name, credential.Server);
        return instance;
    }
}
