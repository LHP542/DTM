using DTM.ViewModels.TreeNodes;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using DTM.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DTM.ViewModels;

/// <summary>
/// Aktions-Gruppe SICHERUNG: Backup, Clone (Sync-Database-ToTest),
/// DB → Samba und der Backup-Browser.
///
/// Zwei der Aktionen gibt es im OdbcDirect-Modus nicht: Copy-Database-ToSamba
/// ist eine reine Dateisystem-Operation und Sync-Database-ToTest eine
/// mehrstufige PowerShell-Orchestrierung — beides ohne SQL-Entsprechung.
/// Die Buttons werden dort ausgeblendet (Phase 10.5); die Guards hier sind
/// das Sicherheitsnetz, falls ein Command doch ausgeloest wird.
/// </summary>
public sealed partial class MainWindowViewModel
{
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
}
