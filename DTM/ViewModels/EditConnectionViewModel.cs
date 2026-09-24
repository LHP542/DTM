using CommunityToolkit.Mvvm.ComponentModel;
using DTM.Config;

namespace DTM.ViewModels;

public sealed partial class EditConnectionViewModel : ViewModelBase
{
    public static IReadOnlyList<DB_SERVER.ServerTyp> ServerTypes { get; } =
        Enum.GetValues<DB_SERVER.ServerTyp>().ToArray();

    public static IReadOnlyList<ServerBackend> Backends { get; } =
        Enum.GetValues<ServerBackend>().ToArray();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMssql))]
    private DB_SERVER.ServerTyp _selectedServerType = DB_SERVER.ServerTyp.MSSQL;

    [ObservableProperty] private string _server = string.Empty;
    [ObservableProperty] private string _user = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _database = "Master";
    [ObservableProperty] private string _connectionString = string.Empty;

    // Phase 9.3: optionale PS-Remoting-Credentials, nur MSSQL — Oracle geht
    // über SSH-Keys, MariaDB ganz ohne PowerShell. Leer = FOC-SQL nimmt sein
    // globales credential.xml.
    [ObservableProperty] private string _remoteUser = string.Empty;
    [ObservableProperty] private string _remotePassword = string.Empty;

    // Phase 10.2: Backend-Wahl pro MSSQL-Server. Default FocSql; OdbcDirect
    // für DMZ-Server ohne WinRM. Oracle ignoriert das Feld.
    [ObservableProperty] private ServerBackend _selectedBackend = ServerBackend.FocSql;

    public bool IsMssql => SelectedServerType == DB_SERVER.ServerTyp.MSSQL;

    public EditConnectionViewModel() { }

    public EditConnectionViewModel(ConnectionEntry entry)
    {
        _selectedServerType = Enum.TryParse<DB_SERVER.ServerTyp>(entry.Key, out var t)
            ? t : DB_SERVER.ServerTyp.MSSQL;
        _server = entry.Server;
        _user = entry.User;
        _password = entry.PlainPassword;
        _database = entry.Database;
        _connectionString = entry.ConnectionString;
        _remoteUser = entry.RemoteUser;
        _remotePassword = entry.PlainRemotePassword;
        _selectedBackend = entry.Backend;
    }

    public ConnectionEntry ToEntry()
    {
        ConnectionEntry e = new()
        {
            Key = SelectedServerType.ToString(),
            Server = Server,
            User = User,
            Database = Database,
            ConnectionString = ConnectionString,
            RemoteUser = IsMssql ? RemoteUser : string.Empty,
            // Nur MSSQL kennt die Backend-Wahl: Oracle hat keinen
            // OdbcDirect-Weg (siehe Phase 10 Design-Doc), MariaDB läuft
            // ohnehin immer direkt. Alles andere fällt auf den Default.
            Backend = IsMssql ? SelectedBackend : ServerBackend.FocSql
        };
        e.PlainPassword = Password;
        // Für Nicht-MSSQL die Remote-Felder bewusst leer speichern —
        // verhindert "vergessene" DPAPI-Blobs, falls der User den Typ von
        // MSSQL auf Oracle oder MariaDB wechselt.
        e.PlainRemotePassword = IsMssql ? RemotePassword : string.Empty;
        return e;
    }
}
