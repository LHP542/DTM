using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DTM.ViewModels.TreeNodes;
using DTM.Config;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using NLog;

namespace DTM.ViewModels;

/// <summary>
/// ViewModel des Hauptfensters. Aufgeteilt in partial classes, damit die
/// Datei nicht wieder auf ~1000 Zeilen anwaechst:
/// <list type="bullet">
/// <item><c>MainWindowViewModel.cs</c> (hier) — Zustand, Server-Baum, Stats.</item>
/// <item><c>.Dispatch.cs</c> — Backend-Wahl (FOC-SQL vs. OdbcDirect) und die
///       gemeinsamen Ausfuehrungspfade aller Aktionen.</item>
/// <item><c>.Backup.cs</c> — Aktions-Gruppe SICHERUNG.</item>
/// <item><c>.Snapshots.cs</c> — Aktions-Gruppen SNAPSHOTS und OLVM.</item>
/// <item><c>.Maintenance.cs</c> — ARCHIVE-LOG, WARTUNG, Recovery-Mode,
///       Cluster-Health.</item>
/// <item><c>.Dialogs.cs</c> — Verbindungen, Sessions, Update-Ablauf.</item>
/// </list>
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private IDTM_DATA _data;

    // Optional: wird von der App via DI gesetzt; in Tests bleibt es null und die
    // Dialog-Aufrufe fallen auf direktes "new" zurueck (Tests stossen die UI-
    // Befehle nicht an, daher reicht das).
    private readonly IServiceProvider? _services;

    private T ResolveOrNew<T>() where T : class, new() =>
        _services?.GetService<T>() ?? new T();

    public ObservableCollection<NodeViewModelBase> RootNodes { get; } = new();

    [ObservableProperty] private NodeViewModelBase? _selectedNode;

    [ObservableProperty] private string _dbName = "—";
    [ObservableProperty] private string _dbHost = "—";
    [ObservableProperty] private string _dbStatus = "—";
    [ObservableProperty] private string _dbVersion = "—";
    [ObservableProperty] private string _dbSize = "—";
    [ObservableProperty] private string _recoveryOrArchiveMode = "—";
    [ObservableProperty] private string _recoveryLabel = "Recovery";
    [ObservableProperty] private string _activeSessionsLabel = "Aktive Sessions: —";
    [ObservableProperty] private string _activeSessionsCount = "0";
    [ObservableProperty] private string _statusBar = "Bereit";
    [ObservableProperty] private string _backupButtonText = "Backup";

    [ObservableProperty] private bool _archiveLogOnEnabled;
    [ObservableProperty] private bool _archiveLogOffEnabled;

    // Get-ClusterHealthStatus ist MSSQL-only (Always-On/Failover-Cluster).
    // Bei Oracle-Selection blenden wir den Button ganz aus.
    [ObservableProperty] private bool _clusterHealthVisible;

    // Backup-Browser ist in v1 MSSQL-only — Oracle-Tab blendet die ganze
    // Gruppe aus, bis ein RMAN-Wrapper kommt.
    [ObservableProperty] private bool _backupBrowserVisible;

    // Wartungs-Gruppe (DBCC CHECKDB / Index-Rebuild / Shrink-Log) ist
    // T-SQL-spezifisch und nur fuer MSSQL sichtbar.
    [ObservableProperty] private bool _maintenanceVisible;

    // Recovery-Mode-Dropdown im Info-Card (MSSQL-only). Oracle zeigt
    // stattdessen den read-only ArchiveLogMode-TextBlock.
    [ObservableProperty] private bool _recoveryModeVisible;
    [ObservableProperty] private string _recoveryModeSelected = "FULL";

    // Phase 10.5: die zwei bei OdbcDirect nicht verfuegbaren Actions —
    // Copy-Database-ToSamba (FS-Operation) und Sync-Database-ToTest
    // (Multi-Step-PS-Orchestrierung). Default true, damit bestehende
    // FocSql-Server unveraendert alles sehen; bei OdbcDirect-Server-
    // Auswahl setzt der ApplyStats-Pfad die Flags auf false.
    [ObservableProperty] private bool _copyToSambaVisible = true;
    [ObservableProperty] private bool _syncToTestVisible = true;

    // Phase 11: OLVM-Aktionen (VM-Snapshot per Ansible) sind Oracle-only.
    // Bei MSSQL-Selection ausgeblendet — der Ansible-Weg macht dort keinen
    // Sinn (SQL-Server hat keine OLVM-VM-Snapshots).
    [ObservableProperty] private bool _olvmVisible;

    public IReadOnlyList<string> RecoveryModeOptions { get; } =
        new[] { "FULL", "SIMPLE", "BULK_LOGGED" };

    // Schutz vor Rekursion: ApplyStats setzt RecoveryModeSelected aus dem
    // Server-Stand — der ComboBox-Selection-Changed-Handler darf das nicht
    // als User-Aktion missverstehen und Set-DbRecoveryMode aufrufen.
    // Ausgewertet in MainWindowViewModel.Maintenance.cs.
    private bool _settingRecoveryModeInternally;
    private string _lastSyncedRecoveryMode = string.Empty;

    private List<Session> _currentSessions = new();

    // Initial-Setup der pwsh-Session:
    //  1. ExecutionPolicy für DIESEN Prozess auf Bypass (nur Runspace-lokal,
    //     ändert nichts am System, braucht keine Admin-Rechte). Sonst scheitert
    //     der Modul-Import an "running scripts is disabled on this system".
    //  2. credential.xml muss existieren — das FOC-SQL-Modul nutzt sie für sein
    //     eigenes Remoting/Credential-Handling. Fehlt sie → klare Meldung.
    //  3. FOC-SQL-Modul frisch von Samba laden — darüber laufen alle Aktionen.
    //     Das Modul baut sein Remoting zu den Servern selbst auf; DTM hält
    //     KEINE eigene PSSession mehr.
    public string ShellInitialCommand =>
        "try { Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force -ErrorAction Stop } catch {}; " +
        "if (-not (Test-Path \"$env:USERPROFILE\\credential.xml\")) { " +
        "  Write-Error 'credential.xml im Benutzerprofil fehlt. " +
        "Bitte einmalig erstellen: Get-Credential | Export-Clixml \"$env:USERPROFILE\\credential.xml\"'; " +
        "  return " +
        "}; " +
        DTM.Data.Terminal.FocSqlRuntime.BuildImportSnippet() + "; " +
        "Write-Host 'FOC-SQL Modul geladen. Bereit.'";

    public MainWindowViewModel(
        IDTM_DATA data,
        IReadOnlyList<DB_SERVER> servers,
        IServiceProvider? services = null)
    {
        _data = data;
        _services = services;
        BuildRootNodes(servers);
        DTM.Data.Terminal.TerminalBus.LineEmitted += OnTerminalLineEmitted;
    }

    // Phase 7.3: VERSION_MISMATCH-Pattern aus dem pwsh-Stream spiegeln. FOC-SQL
    // wirft das, sobald das MSSQL-Modul auf dem Zielserver zu alt ist — der User
    // sieht es sofort im StatusBar statt nur tief im pwsh-Log.
    // Format: "VERSION_MISMATCH: MSSQL-Modul auf 'HOSTNAME' (gefunden: x.y.z) ...".
    private static readonly System.Text.RegularExpressions.Regex _versionMismatchRx =
        new(@"VERSION_MISMATCH:.*'(?<host>[^']+)'.*gefunden:\s*(?<found>[^)\s]+)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private void OnTerminalLineEmitted(object? sender, DTM.Data.Terminal.TerminalLineEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Line) || e.Line.IndexOf("VERSION_MISMATCH", StringComparison.Ordinal) < 0)
            return;
        var m = _versionMismatchRx.Match(e.Line);
        string text = m.Success
            ? $"⚠ MSSQL-Modul auf '{m.Groups["host"].Value}' veraltet ({m.Groups["found"].Value}). Bitte PS-Sitzung auf dem Server oeffnen."
            : "⚠ MSSQL-Versionskonflikt — siehe pwsh-Tab.";
        Dispatcher.UIThread.Post(() => StatusBar = text);
    }

    private void BuildRootNodes(IReadOnlyList<DB_SERVER> servers)
    {
        RootNodes.Clear();
        foreach (var group in servers
                     .GroupBy(s => s.Typ)
                     .OrderBy(g => g.Key.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            var groupNode = new ServerGroupNodeViewModel(group.Key);
            foreach (var server in group.OrderBy(
                         s => s.serverCredential?.Server ?? string.Empty,
                         StringComparer.OrdinalIgnoreCase))
            {
                groupNode.Children.Add(new ServerNodeViewModel(server.Identity, _data));
            }
            RootNodes.Add(groupNode);
        }
    }

    partial void OnSelectedNodeChanged(NodeViewModelBase? value)
    {
        ArchiveLogOnEnabled = false;
        ArchiveLogOffEnabled = false;
        ClusterHealthVisible = false;
        BackupBrowserVisible = false;
        MaintenanceVisible = false;
        RecoveryModeVisible = false;
        // 10.5: bei OdbcDirect-DB werden Copy/Sync ausgeblendet (Ausgangs-Default
        // = sichtbar; ApplyStats setzt bei OdbcDirect auf false).
        CopyToSambaVisible = true;
        SyncToTestVisible = true;
        // 11: OLVM-Aktionen sind Oracle-only, wird in ApplyStats gesetzt.
        OlvmVisible = false;

        switch (value)
        {
            case ServerGroupNodeViewModel:
                // Statische Gruppen-Container — Selektion macht nichts (Children
                // sind beim Aufbau bereits eingehaengt; IsExpanded steuert Anzeige).
                break;
            case ServerNodeViewModel server:
                _ = LoadServerAsync(server);
                break;
            case DatabaseNodeViewModel db:
                _ = LoadStatsAsync(db);
                break;
        }
    }

    private static async Task LoadServerAsync(ServerNodeViewModel server)
    {
        await server.EnsureChildrenLoadedAsync();
        server.IsExpanded = true;
    }

    private async Task LoadStatsAsync(DatabaseNodeViewModel db)
    {
        StatusBar = $"Lade Stats für {db.Database.Name}…";
        try
        {
            Database_Stats stats = await Task.Run(() => _data.get_Database_Stats(db.ServerIdentity, db.Database));
            await Dispatcher.UIThread.InvokeAsync(() => ApplyStats(stats));
            StatusBar = "Bereit";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Stats für {db.Database.Name} fehlgeschlagen.");
            StatusBar = $"Fehler: {ex.Message}";
        }
    }

    internal void ApplyStats(Database_Stats stats)
    {
        _currentSessions = stats.Sessions ?? new List<Session>();
        ActiveSessionsCount = _currentSessions.Count.ToString();
        ActiveSessionsLabel = $"Aktive Sessions: {_currentSessions.Count}";

        if (stats is Database_Stats_MSSQL m)
        {
            bool recoveryOn = string.Equals(m.RecorveryModel, "FULL", StringComparison.OrdinalIgnoreCase);
            ArchiveLogOnEnabled  = !recoveryOn;
            ArchiveLogOffEnabled =  recoveryOn;
            ClusterHealthVisible = true;
            BackupBrowserVisible = true;
            MaintenanceVisible   = true;

            // 10.5: Copy-ToSamba + Sync-Test brauchen den FOC-SQL-Server-Weg
            // (FS-Op bzw. PS-Orchestrierung). Bei OdbcDirect ausblenden.
            if (SelectedNode is DatabaseNodeViewModel selDb
                && _data.Servers.FirstOrDefault(s => s.Identity == selDb.ServerIdentity)?.Backend == ServerBackend.OdbcDirect)
            {
                CopyToSambaVisible = false;
                SyncToTestVisible  = false;
            }
            BackupButtonText = "Backup";
            DbName = m.Name ?? "—";
            DbHost = m.Server ?? "—";
            DbStatus = m.State ?? "—";
            DbVersion = m.CompatibllityLevel.ToString();
            // "Größe" = Gesamtgröße der DB (Daten + Log), damit der Wert mit der
            // SSMS-Datenbankeigenschaft "Größe" übereinstimmt. DataSizeMB wäre nur
            // die Summe der Datendateien (type=0) und ließe das Transaktionslog außen
            // vor — bei FULL-Recovery ohne Log-Backup weicht das massiv ab.
            DbSize = $"{m.TotalSizeMB.ToString("N2", new CultureInfo("de-DE"))} MB";//System.Globalization.CultureInfo.InvariantCulture)} MB";
            RecoveryLabel = "Recovery";
            RecoveryOrArchiveMode = m.RecorveryModel ?? "—";

            // Dropdown auf aktuellen Server-Stand setzen, ohne den User-
            // Change-Pfad zu triggern (Suppression-Flag).
            SyncRecoveryModeFromStats(m.RecorveryModel);
        }
        else if (stats is Database_Stats_ORACLE o)
        {
            bool archiveOn = string.Equals(o.ArchiveLogMode, "ARCHIVELOG", StringComparison.OrdinalIgnoreCase);
            ArchiveLogOnEnabled  = !archiveOn;
            ArchiveLogOffEnabled =  archiveOn;
            BackupButtonText = "Dump";
            DbName = o.InstanceName ?? "—";
            DbHost = o.Server ?? "—";
            DbStatus = o.State ?? "—";
            DbVersion = o.OracleVersion ?? "—";
            DbSize = $"{o.DataSizeMB.ToString(System.Globalization.CultureInfo.InvariantCulture)} MB";
            RecoveryLabel = "ArchiveLog";
            RecoveryOrArchiveMode = o.ArchiveLogMode ?? "—";
            // Phase 11: OLVM-Snapshot-Gruppe nur bei Oracle einblenden.
            OlvmVisible = true;
        }
    }

    /// <summary>
    /// Nach dem Schliessen des Verbindungsmanagers: Server-Liste und
    /// Datenschicht komplett neu aufbauen und die Info-Card leeren.
    /// </summary>
    private void ReloadFromStores()
    {
        List<DB_SERVER> newServers = new();
        foreach (DTM.Config.ConnectionEntry entry in DTM.Config.ConnectionStore.Load())
        {
            if (Enum.TryParse<DB_SERVER.ServerTyp>(entry.Key, ignoreCase: true, out var typ))
                newServers.Add(new DB_SERVER(typ, entry.ToCredential(), entry.Backend));
        }

        _data = new DTM_DATA(newServers, new ODBC_Factory());

        SelectedNode = null;
        BuildRootNodes(newServers);

        DbName = "—"; DbHost = "—"; DbStatus = "—"; DbVersion = "—";
        DbSize = "—"; RecoveryOrArchiveMode = "—"; ActiveSessionsCount = "0";
        ArchiveLogOnEnabled = false;
        ArchiveLogOffEnabled = false;
        ClusterHealthVisible = false;
        BackupBrowserVisible = false;
        MaintenanceVisible = false;
        RecoveryModeVisible = false;
        CopyToSambaVisible = true;
        SyncToTestVisible = true;
        OlvmVisible = false;
        StatusBar = "Verbindungen aktualisiert.";
        _logger.Debug("Verbindungen neu geladen: {0} Server.", newServers.Count);
    }

    private static Window? GetMainWindow()
        => (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
