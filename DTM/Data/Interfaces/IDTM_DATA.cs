namespace DTM
{
    public interface IDTM_DATA
    {
        /// <summary>
        /// Alle registrierten Server-Verbindungen. Wird vom Tree-Aufbau im
        /// MainWindowViewModel iteriert (Phase 6: ein Knoten pro Server,
        /// gruppiert nach Typ).
        /// </summary>
        IReadOnlyList<DB_SERVER> Servers { get; }

        /// <summary>
        /// Datenbank-Liste eines konkreten Servers (identifiziert ueber
        /// <see cref="ServerIdentity"/>, also Typ + Hostname).
        /// </summary>
        List<Database_Info> get_Database_Names(ServerIdentity identity);

        /// <summary>
        /// Statistiken einer Datenbank auf einem konkreten Server.
        /// </summary>
        Database_Stats get_Database_Stats(ServerIdentity identity, Database_Info database);

        /// <summary>
        /// Phase 10.4: liefert den <see cref="Data.Mssql.OdbcMssqlActionService"/>
        /// fuer den benannten Server. Nutzt die interne <see cref="IODBC_Factory"/>
        /// (dieselbe Cache-Instanz wie Stats/Namen-Abfragen — kein Doppel-Connect).
        /// Wirft <see cref="InvalidOperationException"/> wenn der Server nicht
        /// MSSQL ist (Oracle hat keinen ODBC-Direct-Weg).
        /// </summary>
        Data.Mssql.OdbcMssqlActionService GetMssqlActions(ServerIdentity identity);

        /// <summary>
        /// Phase 11.3: liefert den <see cref="Data.Olvm.OlvmSnapshotService"/>
        /// fuer den benannten Oracle-Server. Baut intern einen frischen
        /// <see cref="ORACLE.REST"/>-Client — der Service disposed ihn selbst.
        /// Wirft <see cref="InvalidOperationException"/> wenn der Server nicht
        /// Oracle ist.
        /// </summary>
        Data.Olvm.OlvmSnapshotService GetOlvmSnapshotService(ServerIdentity identity);
    }
}
