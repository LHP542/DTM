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
    [ObservableProperty] private string _updateSource = string.Empty;

    public ConnectionManagerViewModel()
    {
        foreach (ConnectionEntry e in ConnectionStore.Load())
            Connections.Add(e);

        FocSqlConfig foc = AppSettingsStore.LoadFocSql();
        _sambaSource = foc.SambaSource;
        _modulePath = foc.ModulePath;
        _updateSource = foc.UpdateSource;

        _logger.Debug("ConnectionManager: {0} Verbindungen geladen.", Connections.Count);
    }

    public void SaveFocSql()
    {
        FocSqlConfig config = new() { SambaSource = SambaSource, ModulePath = ModulePath, UpdateSource = UpdateSource };
        AppSettingsStore.SaveFocSql(config);
        FocSqlRuntime.Current = config;
        TerminalBus.SendScript(FocSqlRuntime.BuildImportSnippet());
        _logger.Info("FOC-SQL: SambaSource={0}, ModulePath={1}", SambaSource, ModulePath);
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
                e.ToCredential()))
            .ToList();
        TerminalBus.SetCredMap(servers);
    }
}
