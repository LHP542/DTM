using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DTM.ViewModels.TreeNodes;
using DTM.Config;
using DTM.Views;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace DTM.ViewModels;

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

    public IReadOnlyList<string> RecoveryModeOptions { get; } =
        new[] { "FULL", "SIMPLE", "BULK_LOGGED" };

    // Schutz vor Rekursion: ApplyStats setzt RecoveryModeSelected aus dem
    // Server-Stand — der ComboBox-Selection-Changed-Handler darf das nicht
    // als User-Aktion missverstehen und Set-DbRecoveryMode aufrufen.
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
            BackupButtonText = "Backup";
            DbName = m.Name ?? "—";
            DbHost = m.Server ?? "—";
            DbStatus = m.State ?? "—";
            DbVersion = m.CompatibllityLevel.ToString();
            DbSize = $"{m.DataSizeMB.ToString(System.Globalization.CultureInfo.InvariantCulture)} MB";
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
        }
    }

    [RelayCommand]
    private async Task Backup()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;

        // OdbcDirect: kein Scheduling (Task-Scheduler-Weg des FOC-SQL-Moduls
        // faellt weg). Backup laeuft sofort.
        var odbc = TryGetOdbcActions(db);
        if (odbc is not null)
        {
            await RunOdbcActionAsync("Backup", db.Database.Name,
                async onInfo =>
                {
                    string path = await odbc.BackupAsync(db.Database.Name, onInfo).ConfigureAwait(false);
                    onInfo($"Backup-Datei: {path}");
                });
            return;
        }

        await RunDbActionAsync("Backup-Database", db, "Backup");
    }

    [RelayCommand]
    private async Task Clone()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;
        // Sync-Database-ToTest ist Multi-Step-PS-Orchestrierung, kein
        // SQL-Weg — OdbcDirect kann das nicht, Button wird in 10.5 fuer
        // OdbcDirect ausgeblendet. Falls doch durchgerutscht: klarer
        // Hinweis, kein Silent-Fail.
        var odbc = TryGetOdbcActions(db);
        if (odbc is not null)
        {
            DTM.Data.Terminal.TerminalBus.InjectNotice(
                "[Clone (Sync-Database-ToTest) ist bei OdbcDirect nicht verfuegbar — nur ueber FocSql-Server.]");
            return;
        }

        await RunDbActionAsync("Sync-Database-ToTest", db, "Clone");
    }

    [RelayCommand]
    private async Task Snapshot()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;

        var odbc = TryGetOdbcActions(db);
        if (odbc is not null)
        {
            await RunOdbcActionAsync("Snapshot", db.Database.Name,
                async onInfo =>
                {
                    string name = await odbc.CreateSnapshotAsync(db.Database.Name, onInfo).ConfigureAwait(false);
                    onInfo($"Snapshot: {name}");
                });
            return;
        }

        await RunDbActionAsync("Set-Snapshot", db, "Snapshot");
    }

    /// <summary>
    /// Gemeinsamer Pfad für Backup/Clone/Snapshot: Zeit abfragen, dann die
    /// passende FOC-SQL-Modulfunktion über den TerminalBus im pwsh-Tab aufrufen.
    /// Das Modul übernimmt Remoting, Credential-Handling und Scheduling.
    /// </summary>
    private async Task RunDbActionAsync(string focFunction, DatabaseNodeViewModel db, string label)
    {
        TimePickResult pick = await PickTimeAsync();
        if (pick.Cancelled) return;

        DateTime? when = pick.When; // null = sofort
        string whenText = when is { } w ? $"um {w:g}" : "sofort";
        StatusBar = $"{label} für {db.Database.Name} {whenText} …";

        DTM.Data.Terminal.TerminalBus.RunFocSqlAction(
            functionName: focFunction,
            database: ModuleDatabaseId(db),
            when: when,
            title: $"{label} {db.Database.Name}",
            server: ServerParamFor(db),
            onUnavailable: () =>
                Dispatcher.UIThread.Post(() =>
                    StatusBar = $"{label} nicht möglich: pwsh-Tab ist nicht bereit."));

        StatusBar = $"{label} für {db.Database.Name} ausgelöst.";
    }

    // Phase 10.4: liefert den OdbcMssqlActionService, wenn der Server der
    // DB im OdbcDirect-Modus ist. Sonst null → Aufrufer geht den FOC-SQL-Weg.
    // Fuer Oracle immer null (kein OdbcDirect fuer Oracle).
    private DTM.Data.Mssql.OdbcMssqlActionService? TryGetOdbcActions(DatabaseNodeViewModel db)
    {
        var server = _data.Servers.FirstOrDefault(s => s.Identity == db.ServerIdentity);
        if (server?.Backend != ServerBackend.OdbcDirect) return null;
        if (server.Typ != DB_SERVER.ServerTyp.MSSQL) return null;
        return _data.GetMssqlActions(db.ServerIdentity);
    }

    // Phase 10.4: einheitlicher Ausfuehrungspfad fuer OdbcDirect-Actions.
    // Statusbar + Notice-Header/-Footer, Exception-Handling, Live-Output
    // per InjectNotice — der pwsh-Tab sieht die ODBC-Aktion wie eine
    // FOC-SQL-Aktion (nur ohne Live-Stream aus dem Modul).
    private async Task RunOdbcActionAsync(string label, string dbName, Func<Action<string>, Task> action)
    {
        StatusBar = $"{label} für {dbName} …";
        DTM.Data.Terminal.TerminalBus.InjectNotice($"[{label} für {dbName} (OdbcDirect)]");
        try
        {
            Action<string> onInfo = t => DTM.Data.Terminal.TerminalBus.InjectNotice($"  {t}");
            await action(onInfo).ConfigureAwait(false);
            DTM.Data.Terminal.TerminalBus.InjectNotice($"[{label} fertig für {dbName}]");
            await Dispatcher.UIThread.InvokeAsync(() =>
                StatusBar = $"{label} für {dbName} fertig.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "{0} fuer {1} fehlgeschlagen", label, dbName);
            DTM.Data.Terminal.TerminalBus.InjectNotice($"[FEHLER: {ex.Message}]");
            await Dispatcher.UIThread.InvokeAsync(() =>
                StatusBar = $"{label} fehlgeschlagen: {ex.Message}");
        }
    }

    /// <summary>
    /// Bezeichner, den die FOC-SQL-Modulfunktionen erwarten:
    /// MSSQL → DB-Name, Oracle → FQDN (das Modul baut daraus 'oracle@&lt;FQDN&gt;'
    /// als SSH-Ziel). Fällt bei fehlendem FQDN auf den Namen zurück.
    /// </summary>
    internal static string ModuleDatabaseId(DatabaseNodeViewModel db) =>
        db.ServerTyp == DB_SERVER.ServerTyp.MSSQL
            ? db.Database.Name
            : (string.IsNullOrWhiteSpace(db.Database.FQDN) ? db.Database.Name : db.Database.FQDN!);

    /// <summary>
    /// Liefert den Server-Hostname fuer den FOC-SQL -Server-Parameter:
    /// MSSQL → konkreter Hostname (mehrere MSSQL-Server unterscheidbar).
    /// Oracle → <c>null</c> (Oracle adressiert ueber FQDN im -Database-Param,
    /// das -Server-Argument geht an die DTM-Wrapper, die es bei Oracle ignorieren).
    /// </summary>
    internal static string? ServerParamFor(DatabaseNodeViewModel db) =>
        db.ServerTyp == DB_SERVER.ServerTyp.MSSQL
            ? db.ServerIdentity.Server
            : null;

    [RelayCommand]
    private void DbToSamba()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;
        // File-System-Op, kein SQL-Weg — 10.5 blendet den Button aus.
        // Sicherheitsnetz: falls doch durchgerutscht, klarer Hinweis.
        if (TryGetOdbcActions(db) is not null)
        {
            DTM.Data.Terminal.TerminalBus.InjectNotice(
                "[Copy-Database-ToSamba ist bei OdbcDirect nicht verfuegbar — nur ueber FocSql-Server.]");
            return;
        }
        RunSimpleAction("Copy-Database-ToSamba", db, "", "DB → Samba");
    }

    [RelayCommand]
    private async Task RestoreSnapshot()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;

        // OdbcDirect: Auswahl-Dialog fuer Snapshot kommt in Phase 10.4d
        // (MssqlSnapshotSelectWindow). Bis dahin nur eine klare Meldung —
        // der FOC-SQL-Weg wuerde in der DMZ scheitern.
        if (TryGetOdbcActions(db) is not null)
        {
            DTM.Data.Terminal.TerminalBus.InjectNotice(
                "[Restore-Snapshot: Snapshot-Auswahl-Dialog folgt in Phase 10.4d.]");
            return;
        }

        // Oracle: Vorab Restore-Vorschau-Dialog mit Restore-Points und
        // PDB-Liste + Multi-PDB-Warnung. MSSQL ueberspringt das.
        if (db.ServerTyp == DB_SERVER.ServerTyp.ORACLE)
        {
            Window? owner = GetMainWindow();
            if (owner is null || _services is null) return;

            OracleRestoreSelectViewModel vm =
                _services.GetRequiredService<OracleRestoreSelectViewModel>();
            OracleRestoreSelectWindow dlg = new() { DataContext = vm };

            // LoadAsync nicht awaiten — der Dialog geht sofort auf mit
            // Spinner, die Daten landen im UI sobald sie da sind.
            _ = vm.LoadAsync(ModuleDatabaseId(db));

            bool ok = await dlg.ShowDialog<bool>(owner);
            if (!ok) return;
        }

        RunSimpleAction("Restore-Snapshot", db, "", "Restore Snapshot");
    }

    [RelayCommand]
    private void RemoveSnapshot()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;
        if (TryGetOdbcActions(db) is not null)
        {
            DTM.Data.Terminal.TerminalBus.InjectNotice(
                "[Remove-Snapshot: Snapshot-Auswahl-Dialog folgt in Phase 10.4d.]");
            return;
        }
        RunSimpleAction("Remove-Snapshot", db, "", "Remove Snapshot");
    }

    // Set-Archive-Log dispatched im FOC-SQL-Modul nach DB-Typ:
    //   MSSQL  -> Database-Set-Recovery-Mode -Recovery FULL/SIMPLE
    //   Oracle -> /mnt/dbmgmt/scripts/archivelog-on.sh / -off.sh
    // Die "Log An/Aus"-Labels sind dadurch Oracle-zentriert; fuer MSSQL ist
    // es semantisch ein Recovery-Mode-Toggle. Akzeptierte Doppelnutzung
    // (siehe CLAUDE.md / Roadmap 1.1); fuer MSSQL bringt 3.4 einen dedizierten
    // Recovery-Mode-Dropdown als saubere Alternative.
    [RelayCommand]
    private async Task ArchiveLogOn()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;
        ArchiveLogOnEnabled = false;
        ArchiveLogOffEnabled = false;

        var odbc = TryGetOdbcActions(db);
        if (odbc is not null)
        {
            await RunOdbcActionAsync("ArchiveLog An", db.Database.Name,
                onInfo => odbc.SetArchiveLogAsync(db.Database.Name, on: true, onInfo));
        }
        else
        {
            RunSimpleAction("Set-Archive-Log", db, "", "ArchiveLog An");
        }
        _ = Task.Delay(TimeSpan.FromSeconds(8))
                .ContinueWith(_ => Dispatcher.UIThread.InvokeAsync(() => LoadStatsAsync(db)));
    }

    [RelayCommand]
    private async Task ArchiveLogOff()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;
        ArchiveLogOnEnabled = false;
        ArchiveLogOffEnabled = false;

        var odbc = TryGetOdbcActions(db);
        if (odbc is not null)
        {
            await RunOdbcActionAsync("ArchiveLog Aus", db.Database.Name,
                onInfo => odbc.SetArchiveLogAsync(db.Database.Name, on: false, onInfo));
        }
        else
        {
            RunSimpleAction("Set-Archive-Log", db, "-Off", "ArchiveLog Aus");
        }
        _ = Task.Delay(TimeSpan.FromSeconds(8))
                .ContinueWith(_ => Dispatcher.UIThread.InvokeAsync(() => LoadStatsAsync(db)));
    }

    /// <summary>
    /// Aktionen ohne Zeitplanung (Restore/Remove/Archive/Samba). Teils
    /// interaktiv — der Output und etwaige Prompts erscheinen im pwsh-Tab,
    /// Antworten tippt der User in die Befehlszeile.
    /// </summary>
    private void RunSimpleAction(string focFunction, DatabaseNodeViewModel db, string extraArgs, string label)
    {
        StatusBar = $"{label} für {db.Database.Name} …";
        DTM.Data.Terminal.TerminalBus.RunFocSqlSimple(
            functionName: focFunction,
            database: ModuleDatabaseId(db),
            extraArgs: extraArgs,
            title: $"{label} {db.Database.Name}",
            server: ServerParamFor(db),
            onUnavailable: () =>
                Dispatcher.UIThread.Post(() =>
                    StatusBar = $"{label} nicht möglich: pwsh-Tab ist nicht bereit."));
        StatusBar = $"{label} für {db.Database.Name} gestartet — siehe Shell-Tab.";
    }

    [RelayCommand]
    private async Task ManageConnections()
    {
        Window? owner = GetMainWindow();
        if (owner is null) return;
        ConnectionManagerWindow dlg = new() { DataContext = ResolveOrNew<ConnectionManagerViewModel>() };
        await dlg.ShowDialog(owner);
        ReloadFromStores();
    }

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
        StatusBar = "Verbindungen aktualisiert.";
        _logger.Debug("Verbindungen neu geladen: {0} Server.", newServers.Count);
    }

    // Get-ClusterHealthStatus -Server <host> — Always-On/Failover-Cluster-Status.
    // Read-only, MSSQL-only; Output erscheint im pwsh-Tab.
    [RelayCommand]
    private async Task CheckClusterHealth()
    {
        if (string.IsNullOrWhiteSpace(DbHost) || DbHost == "—") return;

        // OdbcDirect: liest sys.dm_hadr_* direkt via ODBC.
        if (SelectedNode is DatabaseNodeViewModel db)
        {
            var odbc = TryGetOdbcActions(db);
            if (odbc is not null)
            {
                await RunOdbcActionAsync("Cluster-Health", DbHost,
                    onInfo => odbc.GetClusterHealthAsync(onInfo));
                return;
            }
        }

        DTM.Data.Terminal.TerminalBus.RunFocSqlServerAction(
            "Get-ClusterHealthStatus", DbHost, "Cluster-Health");
    }

    // --- Recovery-Mode-Dropdown (Phase 3.4, MSSQL-only) ---

    private void SyncRecoveryModeFromStats(string? recoveryFromServer)
    {
        string normalized = recoveryFromServer?.ToUpperInvariant() ?? "FULL";
        if (!RecoveryModeOptions.Contains(normalized))
            normalized = "FULL";

        _settingRecoveryModeInternally = true;
        try
        {
            RecoveryModeSelected = normalized;
            _lastSyncedRecoveryMode = normalized;
            RecoveryModeVisible = true;
        }
        finally
        {
            _settingRecoveryModeInternally = false;
        }
    }

    partial void OnRecoveryModeSelectedChanged(string value)
    {
        if (_settingRecoveryModeInternally) return;
        if (string.Equals(value, _lastSyncedRecoveryMode, StringComparison.OrdinalIgnoreCase)) return;

        _ = OnRecoveryModeChangedByUserAsync(value);
    }

    private async Task OnRecoveryModeChangedByUserAsync(string newMode)
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;
        Window? owner = GetMainWindow();
        if (owner is null) return;

        string warning = string.Equals(newMode, "SIMPLE", StringComparison.OrdinalIgnoreCase)
            ? "\n\nAchtung: Wechsel zu SIMPLE bricht die Log-Chain — Point-in-Time-Restore ist "
              + "ab diesem Zeitpunkt erst nach dem naechsten Voll-Backup wieder moeglich."
            : string.Empty;

        ConfirmWindow dlg = new()
        {
            WindowTitle = "Recovery-Modus aendern?",
            Message = $"Der Recovery-Modus der Datenbank „{db.Database.Name}\" wird von "
                    + $"{_lastSyncedRecoveryMode} auf {newMode} gesetzt.{warning}\n\nFortfahren?",
            ConfirmText = newMode,
            CancelText = "Abbrechen",
        };

        bool ok = await dlg.ShowDialog<bool>(owner);
        if (!ok)
        {
            // User hat abgelehnt — Dropdown auf den zuletzt synchronisierten
            // Server-Stand zurueckdrehen, ohne erneut den Change-Pfad zu triggern.
            _settingRecoveryModeInternally = true;
            try { RecoveryModeSelected = _lastSyncedRecoveryMode; }
            finally { _settingRecoveryModeInternally = false; }
            return;
        }

        var odbc = TryGetOdbcActions(db);
        if (odbc is not null)
        {
            await RunOdbcActionAsync($"Recovery → {newMode}", db.Database.Name,
                onInfo => odbc.SetRecoveryModeAsync(db.Database.Name, newMode, onInfo));
        }
        else
        {
            RunSimpleAction("Set-DbRecoveryMode", db, $"-Recovery {newMode}", $"Recovery -> {newMode}");
        }
        // Optimistisches Update — der naechste DB-Select holt den echten Stand neu.
        _lastSyncedRecoveryMode = newMode;
    }

    // --- Wartung (Phase 3.2, MSSQL-only via Invoke-DbMaintenance) ---

    [RelayCommand]
    private async Task RunCheckDb()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;
        var odbc = TryGetOdbcActions(db);
        if (odbc is not null)
        {
            await RunOdbcActionAsync("DBCC CHECKDB", db.Database.Name,
                onInfo => odbc.CheckDbAsync(db.Database.Name, onInfo));
            return;
        }
        RunSimpleAction("Invoke-DbMaintenance", db, "-CheckDb", "DBCC CHECKDB");
    }

    [RelayCommand]
    private async Task RunIndexRebuild()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;
        var odbc = TryGetOdbcActions(db);
        if (odbc is not null)
        {
            await RunOdbcActionAsync("Index-Rebuild", db.Database.Name,
                onInfo => odbc.IndexRebuildAsync(db.Database.Name, onInfo));
            return;
        }
        RunSimpleAction("Invoke-DbMaintenance", db, "-IndexRebuild", "Index-Rebuild");
    }

    [RelayCommand]
    private async Task RunShrinkLog()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;

        Window? owner = GetMainWindow();
        if (owner is null) return;

        ConfirmWindow dlg = new()
        {
            WindowTitle = "Logdatei verkleinern?",
            Message = $"Die Log-Datei der Datenbank „{db.Database.Name}\" wird per DBCC SHRINKFILE verkleinert.\n\n"
                    + "Die Funktion schaltet intern auf Recovery-Modus SIMPLE und wieder zurueck — "
                    + "dadurch wird die Log-Chain unterbrochen. Point-in-Time-Restore ab diesem Zeitpunkt "
                    + "ist erst nach dem naechsten Voll-Backup wieder moeglich.\n\nWirklich fortfahren?",
            ConfirmText = "Shrinken",
            CancelText = "Abbrechen",
        };

        bool ok = await dlg.ShowDialog<bool>(owner);
        if (!ok) return;

        var odbc = TryGetOdbcActions(db);
        if (odbc is not null)
        {
            await RunOdbcActionAsync("Shrink-Log", db.Database.Name,
                onInfo => odbc.ShrinkLogAsync(db.Database.Name, onInfo));
            return;
        }

        RunSimpleAction("Invoke-DbMaintenance", db, "-ShrinkLog", "Shrink-Log");
    }

    // DB-Konfiguration: Dialog mit Query-Store-Toggle, Page-Verify-Dropdown
    // und Compatibility-Reset (Phase 5.1/5.3, MSSQL-only). Aktuelle Werte
    // aus Database_Stats_MSSQL als Vorauswahl.
    [RelayCommand]
    private async Task OpenDbConfiguration()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;
        if (db.ServerTyp != DB_SERVER.ServerTyp.MSSQL) return;

        Window? owner = GetMainWindow();
        if (owner is null || _services is null) return;

        DbConfigurationViewModel vm = _services.GetRequiredService<DbConfigurationViewModel>();
        // Aktuelle Werte aus dem letzten ApplyStats-Lauf rekonstruieren (was im
        // Info-Card sichtbar ist). Stats nicht neu abrufen — der User sieht ja
        // die gleichen Werte, die er gerade angeschaut hat.
        int currentCompat = int.TryParse(DbVersion, out int v) ? v : 0;
        // PageVerify ist heute kein Property im VM — wir uebergeben null und
        // lassen das ViewModel auf CHECKSUM-Default fallen, bis der User waehlt.
        vm.Configure(
            database: ModuleDatabaseId(db),
            serverHost: ServerParamFor(db),
            currentPageVerify: null,
            currentCompatibility: currentCompat,
            odbcActions: TryGetOdbcActions(db));

        DbConfigurationWindow dlg = new() { DataContext = vm };
        await dlg.ShowDialog(owner);
    }

    // Backup-Browser: Dialog mit allen .bak-Dateien der selektierten MSSQL-DB,
    // mit Restore-Knopf (WITH REPLACE). MSSQL-only in v1.
    [RelayCommand]
    private async Task OpenBackupBrowser()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;
        if (db.ServerTyp != DB_SERVER.ServerTyp.MSSQL) return;

        Window? owner = GetMainWindow();
        if (owner is null || _services is null) return;

        BackupBrowserViewModel vm =
            _services.GetRequiredService<BackupBrowserViewModel>();
        BackupBrowserWindow dlg = new() { DataContext = vm };

        // Spinner ist sofort sichtbar, Daten laden parallel.
        _ = vm.LoadAsync(ModuleDatabaseId(db), ServerParamFor(db), TryGetOdbcActions(db));

        await dlg.ShowDialog(owner);
    }

    [RelayCommand]
    private async Task ShowSessions()
    {
        Window? owner = GetMainWindow();
        if (owner is null) return;

        SessionsViewModel vm = ResolveOrNew<SessionsViewModel>();
        vm.SetSessions(_currentSessions);
        if (SelectedNode is DatabaseNodeViewModel db)
            vm.Configure(ModuleDatabaseId(db), db.Database.Name, TryGetOdbcActions(db));
        SessionsWindow dlg = new SessionsWindow { DataContext = vm };
        await dlg.ShowDialog(owner);
    }

    private async Task<TimePickResult> PickTimeAsync()
    {
        Window? owner = GetMainWindow();
        if (owner is null) return TimePickResult.Cancel();

        TimePickerWindow dlg = new TimePickerWindow { DataContext = ResolveOrNew<TimePickerViewModel>() };
        return await dlg.ShowDialog<TimePickResult>(owner);
    }

    public async Task CheckForUpdateAsync()
    {
        try
        {
            string src = DTM.Data.Terminal.FocSqlRuntime.Current.UpdateSource;
            var newVersion = await DTM.Updater.UpdateService.CheckForUpdateAsync(src);
            if (newVersion is not null)
                await ShowUpdateDialogAsync(newVersion, src);
        }
        catch (Exception ex) { _logger.Warn(ex, "Update-Prüfung fehlgeschlagen."); }
    }

    private async Task ShowUpdateDialogAsync(Version newVersion, string updateSource)
    {
        Window? owner = GetMainWindow();
        if (owner is null) return;

        var current = DTM.Updater.UpdateService.CurrentVersion();
        var notes = await DTM.Updater.UpdateService.LoadReleaseNotesAsync(updateSource, current, newVersion);

        var dlg = new UpdatePromptWindow(newVersion.ToString(), current.ToString(3), notes);
        await dlg.ShowDialog(owner);

        switch (dlg.Result)
        {
            case UpdateDialogResult.ApplyNow:
                _logger.Info("Update wird jetzt angewendet: {0}", newVersion);
                var applyProgress = new Progress<(int Done, int Total, string File)>(p =>
                    StatusBar = $"Update: {p.Done}/{p.Total} — {p.File}");
                await DTM.Updater.UpdateService.ApplyUpdateAsync(updateSource, applyProgress);
                break;
            case UpdateDialogResult.Later:
                _logger.Info("Update auf {0} auf später verschoben (30 min).", newVersion);
                _ = Task.Delay(TimeSpan.FromMinutes(30))
                        .ContinueWith(_ =>
                            Dispatcher.UIThread.InvokeAsync(() =>
                                ShowUpdateDialogAsync(newVersion, updateSource)));
                break;
            case UpdateDialogResult.Skip:
                _logger.Info("Update auf {0} für diese Sitzung übersprungen.", newVersion);
                break;
        }
    }

    private static Window? GetMainWindow()
        => (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
