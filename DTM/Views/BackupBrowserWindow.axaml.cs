using Avalonia.Interactivity;
using DTM.ViewModels;

namespace DTM.Views;

/// <summary>
/// Dialog mit der Liste aller Sicherungen einer Datenbank (<c>.bak</c> bei
/// MSSQL, <c>.sql</c>-Dumps bei MariaDB) + Restore-Knopf. Vor jedem Restore
/// eine harte Bestätigung via <see cref="ConfirmWindow"/>; den Weg dahinter
/// wählt das ViewModel.
/// </summary>
public partial class BackupBrowserWindow : ChromeWindow
{
    public BackupBrowserWindow()
    {
        InitializeComponent();
    }

    private void OnCancel(object? _, RoutedEventArgs e) => Close();

    private async void OnRestore(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BackupBrowserViewModel vm) return;
        if (vm.SelectedBackup is null) return;

        ConfirmWindow dlg = new()
        {
            WindowTitle = "Restore ausführen?",
            Message = $"Die Datenbank „{vm.DatabaseName}\" wird mit dem Backup\n"
                    + $"„{vm.SelectedBackup.Name}\" ({vm.SelectedBackup.SizeDisplay}, "
                    + $"{vm.SelectedBackup.LastWriteTime:yyyy-MM-dd HH:mm}) überschrieben.\n\n"
                    + vm.RestoreNote + " Änderungen seit dem "
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
