namespace DTM
{
    public class DbServer
    {
        public enum ServerTyp
        {
            ORACLE,
            MSSQL,
            PostgreSQL
        }

        public ServerTyp Typ { get; }
        public ServerCredential? serverCredential { get; private set; }

        /// <summary>
        /// Phase 10: Ausfuehrungspfad fuer FOC-SQL-Actions. Fuer MSSQL vom User
        /// waehlbar (FocSql vs. OdbcDirect), fuer Oracle irrelevant und wird
        /// beim Speichern in <see cref="Config.ConnectionEntry"/> auf Default
        /// zurueckgesetzt. Siehe <see cref="ServerBackend"/>.
        /// </summary>
        public ServerBackend Backend { get; }

        /// <summary>
        /// Composite-Identitaet (Typ, Hostname). Wird in Phase 6 zur eindeutigen
        /// Adressierung eines Servers genutzt — frueher reichte der Typ allein
        /// (Dictionary-Key), jetzt koennen mehrere Hosts pro Typ existieren.
        /// </summary>
        public ServerIdentity Identity =>
            new(Typ, serverCredential?.Server ?? string.Empty);

        public DbServer(ServerTyp typ, ServerCredential serverCredential,
                         ServerBackend backend = ServerBackend.FocSql)
        {
            this.Typ = typ;
            this.serverCredential = serverCredential;
            this.Backend = backend;
        }
    }
}
