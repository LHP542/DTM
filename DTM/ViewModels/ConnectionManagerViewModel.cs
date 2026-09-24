using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DTM.Config;
using DTM.Data.Terminal;
using NLog;

namespace DTM.ViewModels;

public sealed partial class ConnectionManagerViewModel : ViewModelBase
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    public ObservableCollection<ConnectionEntry> Connections { get; } = [];

    [ObservableProperty] private ConnectionEntry? _selectedConnection;

    [ObservableProperty] private string _sambaSource = string.Empty;
    [ObservableProperty] private string _modulePath = string.Empty;

    /// <summary>
    /// Update-Quelle: leer = Rollout-Verzeichnis im Netz, <c>https://…</c> =
    /// GitHub. Siehe <see cref="DTM.Updater.UpdateChannel"/>.
    /// </summary>
    [ObservableProperty] private string _updateChannel = string.Empty;

    /// <summary>Platzhalter im Eingabefeld — zeigt, was ohne Eintrag gilt.</summary>
    public static string UpdateChannelPlaceholder =>
        $"leer = {DTM.Updater.UpdateChannel.DefaultFolder}";

    // --- MariaDB-Werkzeuge -------------------------------------------------
    // MariaDB kennt kein BACKUP DATABASE; Dump und Restore laufen ueber die
    // externen Kommandozeilenwerkzeuge. Leer = im PATH suchen.
    [ObservableProperty] private string _mariaDbDumpPath = string.Empty;
    [ObservableProperty] private string _mariaDbClientPath = string.Empty;
    [ObservableProperty] private string _mariaDbBackupRoot = string.Empty;

    public ConnectionManagerViewModel()
    {
        foreach (ConnectionEntry e in ConnectionStore.Load())
            Connections.Add(e);

        FocSqlConfig foc = AppSettingsStore.LoadFocSql();
        _sambaSource = foc.SambaSource;
        _modulePath = foc.ModulePath;
        _updateChannel = foc.UpdateChannel;
        _mariaDbDumpPath = foc.MariaDb.DumpPath;
        _mariaDbClientPath = foc.MariaDb.ClientPath;
        _mariaDbBackupRoot = foc.MariaDb.BackupRoot;

        _logger.Debug("ConnectionManager: {0} Verbindungen geladen.", Connections.Count);
    }

    public void SaveFocSql()
    {
        // Bestehende Einstellungen laden und nur die Felder dieses Fensters
        // ueberschreiben. Vorher wurde hier ein frisches FocSqlConfig gebaut —
        // damit hat jedes Speichern im Verbindungsmanager alles zurueckgesetzt,
        // was sonst noch in der settings.json steht (die REST-API-Optionen
        // etwa, samt Bearer-Token).
        FocSqlConfig config = AppSettingsStore.LoadFocSql();
        config.SambaSource = SambaSource;
        config.ModulePath = ModulePath;
        config.UpdateChannel = UpdateChannel;
        config.MariaDb.DumpPath = MariaDbDumpPath;
        config.MariaDb.ClientPath = MariaDbClientPath;
        config.MariaDb.BackupRoot = MariaDbBackupRoot;

        AppSettingsStore.SaveFocSql(config);
        FocSqlRuntime.Current = config;
        TerminalBus.SendScript(FocSqlRuntime.BuildImportSnippet());
        // Der Kanal wird beim App-Start in den UpdateService gegeben — eine
        // Aenderung greift daher erst beim naechsten Start.
        _logger.Info("FOC-SQL: SambaSource={0}, ModulePath={1}, UpdateChannel={2}",
            SambaSource, ModulePath, string.IsNullOrWhiteSpace(UpdateChannel) ? "(Default)" : UpdateChannel);
    }

    public void AddEntry(ConnectionEntry entry)
    {
        Connections.Add(entry);
        SelectedConnection = entry;
        Save();
        _logger.Debug("Verbindung hinzugefügt: {0}", entry.Key);
    }

    public void UpdateEntry(ConnectionEntry updated)
    {
        int idx = SelectedConnection is not null ? Connections.IndexOf(SelectedConnection) : -1;
        if (idx >= 0) Connections[idx] = updated;
        SelectedConnection = updated;
        Save();
        _logger.Debug("Verbindung aktualisiert: {0}", updated.Key);
    }

    public void DeleteSelected()
    {
        if (SelectedConnection is null) return;
        _logger.Debug("Verbindung gelöscht: {0}", SelectedConnection.Key);
        Connections.Remove(SelectedConnection);
        SelectedConnection = null;
        Save();
    }

    private void Save()
    {
        ConnectionStore.Save([.. Connections]);
        // Phase 9.5: PS-Remoting-Credentials koennen sich mit dem Save geaendert
        // haben (neuer DMZ-Server, Passwort-Rotation, …). $global:DtmCredMap
        // im Runspace muss synchron ziehen — sonst laufen laufende Sessions
        // gegen die alten Credentials.
        var servers = Connections
            .Select(e => new DB_SERVER(
                Enum.TryParse<DB_SERVER.ServerTyp>(e.Key, out var t) ? t : DB_SERVER.ServerTyp.MSSQL,
                e.ToCredential(),
                e.Backend))
            .ToList();
        TerminalBus.SetCredMap(servers);
    }
}
