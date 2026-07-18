using System.Text.Json.Serialization;

namespace DTM.Data.Config;

public sealed class ConnectionEntry
{
    public string Key { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string PasswordProtected { get; set; } = string.Empty;
    public string Database { get; set; } = "Master";
    public string ConnectionString { get; set; } = string.Empty;

    // Phase 9: optionale abweichende PS-Remoting-Credentials. Bestandseintraege
    // ohne diese Felder deserialisieren mit Default = leer und laufen wie
    // vorher gegen das globale credential.xml.
    public string RemoteUser { get; set; } = string.Empty;
    public string RemotePasswordProtected { get; set; } = string.Empty;

    // Phase 10: Ausfuehrungspfad pro Server (FocSql vs. OdbcDirect). Legacy-JSON
    // ohne das Feld → Default = FocSql (Bestandsverhalten). JsonStringEnumConverter,
    // damit die Datei lesbar bleibt.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ServerBackend Backend { get; set; } = ServerBackend.FocSql;

    [JsonIgnore]
    public string PlainPassword
    {
        get => ConnectionStore.Unprotect(PasswordProtected);
        set => PasswordProtected = ConnectionStore.Protect(value);
    }

    [JsonIgnore]
    public string PlainRemotePassword
    {
        get => ConnectionStore.Unprotect(RemotePasswordProtected);
        set => RemotePasswordProtected = ConnectionStore.Protect(value);
    }

    public ServerCredential ToCredential() =>
        new(Server, User, PlainPassword, Database, ConnectionString,
            RemoteUser, PlainRemotePassword);
}
