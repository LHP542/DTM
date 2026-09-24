using DTM.ViewModels.TreeNodes;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using DTM.Data.MariaDb;
using DTM.Views;

namespace DTM.ViewModels;

/// <summary>
/// Aktions-Gruppe WARTUNG für MariaDB.
///
/// <para>Eigene Gruppe statt der MSSQL-Wartung, weil die Befehle andere sind:
/// <c>CHECK</c>, <c>OPTIMIZE</c> und <c>ANALYZE TABLE</c> wirken pro Tabelle,
/// während DBCC CHECKDB und Index-Rebuild die ganze Datenbank nehmen. Die
/// Buttons hängen an <c>MariaDbMaintenanceVisible</c> und sind nur sichtbar,
/// wenn eine MariaDB-Datenbank gewählt ist.</para>
///
/// <para><c>OPTIMIZE TABLE</c> schreibt die Tabelle neu und sperrt sie dabei
/// bei den meisten Engines — deshalb steht davor ein Bestätigungsdialog.
/// <c>CHECK</c> und <c>ANALYZE</c> laufen ohne Rückfrage: Ersteres ist
/// lesend, Letzteres aktualisiert nur die Optimizer-Statistiken.</para>
/// </summary>
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private Task MariaDbCheckTables() =>
        RunMariaDbMaintenanceAsync(MariaDbActionService.TableMaintenance.Check, "CHECK TABLE");

    [RelayCommand]
    private Task MariaDbAnalyzeTables() =>
        RunMariaDbMaintenanceAsync(MariaDbActionService.TableMaintenance.Analyze, "ANALYZE TABLE");

    [RelayCommand]
    private async Task MariaDbOptimizeTables()
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;

        Window? owner = GetMainWindow();
        if (owner is null) return;

        ConfirmWindow dlg = new()
        {
            WindowTitle = "Tabellen optimieren?",
            Message = $"Für alle Tabellen der Datenbank „{db.Database.Name}\" wird OPTIMIZE TABLE "
                    + "ausgeführt.\n\n"
                    + "Die Tabellen werden dabei neu geschrieben und sind je nach Storage-Engine "
                    + "für die Dauer gesperrt. Bei großen Tabellen kann das deutlich dauern und "
                    + "zusätzlichen Plattenplatz brauchen.\n\nWirklich fortfahren?",
            ConfirmText = "Optimieren",
            CancelText = "Abbrechen",
        };

        if (!await dlg.ShowDialog<bool>(owner)) return;

        await RunMariaDbMaintenanceAsync(MariaDbActionService.TableMaintenance.Optimize, "OPTIMIZE TABLE");
    }

    private async Task RunMariaDbMaintenanceAsync(
        MariaDbActionService.TableMaintenance operation, string label)
    {
        if (SelectedNode is not DatabaseNodeViewModel db) return;
        if (db.ServerTyp != DB_SERVER.ServerTyp.MariaDB) return;

        await RunOdbcActionAsync(label, db.Database.Name,
            onInfo => _data.GetMariaDbActions(db.ServerIdentity)
                .RunTableMaintenanceAsync(db.Database.Name, operation, onInfo));
    }
}
