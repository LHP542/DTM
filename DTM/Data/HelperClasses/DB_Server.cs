namespace DTM;

public class DB_SERVER
{
    public enum ServerTyp
    {
        ORACLE,
        MSSQL,
        // Phase 16: direkter Zugriff über MySqlConnector, ohne FOC-SQL.
        // PostgreSQL stand hier früher als Platzhalter — ohne jede
        // Implementierung. Der Typ-Dropdown speist sich direkt aus diesem
        // Enum, ein nicht implementierter Wert war dort also wählbar und
        // lief in der Factory ins Leere. Kommt zurück, wenn er gebaut wird.
        MariaDB
    }

    public ServerTyp Typ { get; }
    public ServerCredential? serverCredential { get; private set; }

    /// <summary>
    /// Phase 10: Ausführungspfad für FOC-SQL-Actions. Für MSSQL vom User
    /// wählbar (FocSql vs. OdbcDirect), für Oracle irrelevant und wird
    /// beim Speichern in <see cref="Config.ConnectionEntry"/> auf Default
    /// zurückgesetzt. Siehe <see cref="ServerBackend"/>.
    /// </summary>
    public ServerBackend Backend { get; }

    /// <summary>
    /// Composite-Identität (Typ, Hostname). Wird in Phase 6 zur eindeutigen
    /// Adressierung eines Servers genutzt — früher reichte der Typ allein
    /// (Dictionary-Key), jetzt können mehrere Hosts pro Typ existieren.
    /// </summary>
    public ServerIdentity Identity =>
        new(Typ, serverCredential?.Server ?? string.Empty);

    public DB_SERVER(ServerTyp typ, ServerCredential serverCredential,
                     ServerBackend backend = ServerBackend.FocSql)
    {
        this.Typ = typ;
        this.serverCredential = serverCredential;
        this.Backend = backend;
    }
}
