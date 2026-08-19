using DTM.ViewModels.TreeNodes;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DTM.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DTM.ViewModels;

/// <summary>
/// Aktions-Gruppen ARCHIVE-LOG und WARTUNG plus der Recovery-Mode-Dropdown
/// und der Cluster-Health-Check.
///
/// Alles hier ausser Archive-Log ist MSSQL-only (T-SQL-spezifisch) und in der
/// UI ueber <c>MaintenanceVisible</c> / <c>RecoveryModeVisible</c> /
/// <c>ClusterHealthVisible</c> ausgeblendet, wenn eine Oracle-DB gewaehlt ist.
/// </summary>
public sealed partial class MainWindowViewModel
{
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
}
