using Avalonia.Interactivity;
using DTM.ViewModels;

namespace DTM.Views;

/// <summary>
/// Dialog mit der Liste aller <c>.bak</c>-Dateien einer MSSQL-Datenbank
/// + Restore-Knopf. Vor jedem Restore eine harte Bestaetigung via
/// <see cref="ConfirmWindow"/>; danach laeuft <c>Invoke-DbRestore</c>
/// im pwsh-Tab.
/// </summary>
public partial class BackupBrowserWindow : ChromeWindow
{
    public BackupBrowserWindow()
    {
        InitializeComponent();
    }

    private void OnTitleClose(object? _, RoutedEventArgs e) => Close();
    private void OnCancel(object? _, RoutedEventArgs e) => Close();

    private async void OnRestore(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BackupBrowserViewModel vm) return;
        if (vm.SelectedBackup is null) return;

        ConfirmWindow dlg = new()
        {
            WindowTitle = "Restore ausfuehren?",
            Message = $"Die Datenbank „{vm.DatabaseName}\" wird mit dem Backup\n"
                    + $"„{vm.SelectedBackup.Name}\" ({vm.SelectedBackup.SizeDisplay}, "
                    + $"{vm.SelectedBackup.LastWriteTime:yyyy-MM-dd HH:mm}) ueberschrieben.\n\n"
                    + "Alle aktiven Sessions werden vorher beendet. Aenderungen seit dem "
                    + "Backup-Zeitpunkt gehen verloren.\n\nWirklich fortfahren?",
            ConfirmText = "Restore",
            CancelText = "Abbrechen",
        };

        bool ok = await dlg.ShowDialog<bool>(this);
        if (!ok) return;

        vm.PerformRestore(vm.SelectedBackup);
        Close();
    }
}
