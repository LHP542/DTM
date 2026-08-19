using DTM.ViewModels.TreeNodes;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using DTM.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DTM.ViewModels;

/// <summary>
/// Aktions-Gruppen SNAPSHOTS und OLVM.
///
/// Drei Snapshot-Welten treffen hier aufeinander:
/// <list type="bullet">
/// <item>MSSQL ueber FOC-SQL — interaktiv im pwsh-Tab (Read-Host im Modul).</item>
/// <item>MSSQL ueber OdbcDirect — Auswahl im <see cref="MssqlSnapshotSelectWindow"/>,
///       weil es im DMZ-Modus keinen interaktiven Prompt gibt.</item>
/// <item>Oracle — Restore-Points mit vorgeschalteter Vorschau (Multi-PDB-Warnung)
///       sowie OLVM-VM-Snapshots per Ansible-Playbook auf DBMANAGER01.</item>
/// </list>
/// </summary>
public sealed partial class MainWindowViewModel
{
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

    [RelayCommand]
    private async Task RestoreSnapshot()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;

        // OdbcDirect: MssqlSnapshotSelectWindow zeigt die Liste; User waehlt
        // Snapshot und Aktion. Bei Restore hier, bei Drop faellt in denselben
        // Dispatcher-Zweig.
        var odbc = TryGetOdbcActions(db);
        if (odbc is not null)
        {
            await ShowMssqlSnapshotDialogAsync(db, odbc, MssqlSnapshotAction.Restore);
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
    private async Task RemoveSnapshot()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;
        var odbc = TryGetOdbcActions(db);
        if (odbc is not null)
        {
            await ShowMssqlSnapshotDialogAsync(db, odbc, MssqlSnapshotAction.Drop);
            return;
        }
        RunSimpleAction("Remove-Snapshot", db, "", "Remove Snapshot");
    }

    // Phase 11.2: OLVM-VM-Snapshot per Ansible-Playbook auf DBMANAGER01.
    // Oracle-only. Destruktiv (DB + VM Shutdown) → ConfirmWindow zuerst.
    [RelayCommand]
    private async Task OlvmSnapshot()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;
        if (db.ServerTyp != DB_SERVER.ServerTyp.ORACLE) return;

        Window? owner = GetMainWindow();
        if (owner is null) return;

        ConfirmWindow dlg = new()
        {
            WindowTitle = "OLVM-Snapshot?",
            Message = $"Für '{db.Database.Name}' wird ein OLVM-VM-Snapshot ausgeführt.\n\n"
                    + "Ablauf (Ansible-Playbook auf DBMANAGER01):\n"
                    + "  1) Datenbank herunterfahren\n"
                    + "  2) VM herunterfahren\n"
                    + "  3) OLVM-Snapshot erstellen\n"
                    + "  4) VM starten\n"
                    + "  5) Datenbank starten\n\n"
                    + "Dauer: mehrere Minuten. Wirklich fortfahren?",
            ConfirmText = "Snapshot ausführen",
            CancelText = "Abbrechen",
        };

        bool ok = await dlg.ShowDialog<bool>(owner);
        if (!ok) return;

        RunSimpleAction("Invoke-OlvmSnapshot", db, "", "OLVM-Snapshot");
    }

    // Phase 11.3/11.6: Snapshot-Liste via OLVM-REST anzeigen.
    // Restore- und Löschen-Buttons im Dialog sind disabled, bis
    // 11.4/11.5 Ansible-Playbooks bereit sind — dann aktivierbar,
    // Command bleibt unveraendert.
    [RelayCommand]
    private Task OlvmRestoreSnapshot() => ShowOlvmSnapshotDialogAsync(MssqlSnapshotAction.Restore);

    [RelayCommand]
    private Task OlvmRemoveSnapshot() => ShowOlvmSnapshotDialogAsync(MssqlSnapshotAction.Drop);

    private async Task ShowOlvmSnapshotDialogAsync(MssqlSnapshotAction initial)
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;
        if (db.ServerTyp != DB_SERVER.ServerTyp.ORACLE) return;

        Window? owner = GetMainWindow();
        if (owner is null || _services is null) return;

        // Die VM-UUID kommt bei Oracle-Nodes aus Database.Id (via OLVM-REST
        // in ORACLE_ODBC.get_Datenbank_Names gesetzt). Ohne UUID → Abbruch
        // mit klarer Meldung; die Snapshots-Route braucht sie zwingend.
        string vmId = db.Database.Id ?? string.Empty;
        if (string.IsNullOrWhiteSpace(vmId))
        {
            StatusBar = $"Keine VM-UUID fuer '{db.Database.Name}' bekannt — Snapshot-Liste nicht ladbar.";
            return;
        }

        OlvmSnapshotSelectViewModel vm =
            _services.GetRequiredService<OlvmSnapshotSelectViewModel>();
        var svc = _data.GetOlvmSnapshotService(db.ServerIdentity);
        OlvmSnapshotSelectWindow dlg = new() { DataContext = vm };
        _ = vm.LoadAsync(db.Database.Name, vmId, svc, initial);

        OlvmSnapshotSelectResult? result =
            await dlg.ShowDialog<OlvmSnapshotSelectResult?>(owner);

        // Restore/Delete werden erst mit 11.4/11.5 verkabelt — die Buttons
        // im Dialog sind aktuell disabled, wir werden hier nie ein Ergebnis
        // ausser null bekommen. Guard trotzdem als Safety-Net.
        if (result is null) return;
        DTM.Data.Terminal.TerminalBus.InjectNotice(
            $"[OLVM Snapshot {result.Action}: '{result.Snapshot.Description}' — Ansible-Playbook noch nicht implementiert (Phase 11.4/11.5).]");
    }

    // Phase 10.4d: OdbcDirect-Weg fuer Snapshot-Restore + Snapshot-Drop.
    // Ein Dialog fuer beide Aktionen — der Aufrufer gibt an, was
    // initial im Fokus stehen soll, der User kann per Button-Klick am
    // Ende immer noch zwischen Restore und Drop entscheiden.
    private async Task ShowMssqlSnapshotDialogAsync(
        DatabaseNodeViewModel db,
        DTM.Data.Mssql.OdbcMssqlActionService odbc,
        MssqlSnapshotAction initial)
    {
        Window? owner = GetMainWindow();
        if (owner is null || _services is null) return;

        MssqlSnapshotSelectViewModel vm =
            _services.GetRequiredService<MssqlSnapshotSelectViewModel>();
        MssqlSnapshotSelectWindow dlg = new() { DataContext = vm };
        _ = vm.LoadAsync(db.Database.Name, odbc, initial);

        MssqlSnapshotSelectResult? result =
            await dlg.ShowDialog<MssqlSnapshotSelectResult?>(owner);
        if (result is null) return;

        switch (result.Action)
        {
            case MssqlSnapshotAction.Restore:
                await RunOdbcActionAsync($"Restore Snapshot '{result.Snapshot.Name}'",
                    db.Database.Name,
                    onInfo => odbc.RestoreSnapshotAsync(db.Database.Name, result.Snapshot.Name, onInfo));
                break;
            case MssqlSnapshotAction.Drop:
                await RunOdbcActionAsync($"Drop Snapshot '{result.Snapshot.Name}'",
                    db.Database.Name,
                    onInfo => odbc.DropSnapshotAsync(result.Snapshot.Name, onInfo));
                break;
        }
    }
}
