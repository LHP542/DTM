using Avalonia.Interactivity;
using DTM.ViewModels;

namespace DTM.Views;

/// <summary>
/// Dialog zur Auswahl eines MSSQL-DB-Snapshots vor Restore oder Drop
/// (Phase 10.4d). Result: <see cref="MssqlSnapshotSelectResult"/> mit
/// der gewaehlten Aktion + Snapshot, oder <c>null</c> bei Abbruch.
/// </summary>
public partial class MssqlSnapshotSelectWindow : ChromeWindow
{
    public MssqlSnapshotSelectWindow()
    {
        InitializeComponent();
    }

    private void OnCancel(object? _, RoutedEventArgs e) => Close(null);

    private void OnRestore(object? _, RoutedEventArgs e)
    {
        if (DataContext is MssqlSnapshotSelectViewModel vm && vm.SelectedSnapshot is { } snap)
            Close(new MssqlSnapshotSelectResult(MssqlSnapshotAction.Restore, snap));
        else
            Close(null);
    }

    private void OnDrop(object? _, RoutedEventArgs e)
    {
        if (DataContext is MssqlSnapshotSelectViewModel vm && vm.SelectedSnapshot is { } snap)
            Close(new MssqlSnapshotSelectResult(MssqlSnapshotAction.Drop, snap));
        else
            Close(null);
    }
}
