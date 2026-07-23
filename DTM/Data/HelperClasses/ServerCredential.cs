namespace DTM
{
    public class ServerCredential(
        string Server = "FOC-SQL01",
        string User = "",
        string Password = "",
        string Datenbank = "Master",
        string ConnectionString = "",
        string RemoteUser = "",
        string RemotePassword = "")
    {
        public string Server { get; set; } = Server;
        public string User { get; set; } = User;
        public string Password { get; set; } = Password;
        public string Datenbank { get; set; } = Datenbank;
        public string ConnectionString { get; set; } = ConnectionString;

        // Phase 9: optionale abweichende Windows-Credentials fuer PowerShell-
        // Remoting (WinRM/PSSession) auf diesen Server. Leer = FOC-SQL nimmt
        // sein globales credential.xml wie bisher. Genutzt fuer DMZ-Server
        // in fremden AD-Zonen.
        public string RemoteUser { get; set; } = RemoteUser;
        public string RemotePassword { get; set; } = RemotePassword;

        public bool HasRemoteCredential =>
            !string.IsNullOrWhiteSpace(RemoteUser) && !string.IsNullOrEmpty(RemotePassword);
    }
}
