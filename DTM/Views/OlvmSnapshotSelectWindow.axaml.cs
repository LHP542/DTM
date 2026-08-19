using Avalonia.Interactivity;
using DTM.ViewModels;

namespace DTM.Views;

/// <summary>
/// Dialog zur Auswahl eines OLVM-VM-Snapshots (Phase 11.6).
/// Restore + Löschen sind aktuell disabled — die Ansible-Playbooks
/// dafuer werden noch gebaut (Phase 11.4/11.5).
///
/// Result: <see cref="OlvmSnapshotSelectResult"/> mit gewaehlter Aktion
/// + Snapshot, oder <c>null</c> bei Abbruch bzw. wenn keine Aktion
/// verfuegbar ist.
/// </summary>
public partial class OlvmSnapshotSelectWindow : ChromeWindow
{
    public OlvmSnapshotSelectWindow()
    {
        InitializeComponent();
    }

    private void OnCancel(object? _, RoutedEventArgs e) => Close(null);

    private void OnRestore(object? _, RoutedEventArgs e)
    {
        if (DataContext is OlvmSnapshotSelectViewModel vm && vm.SelectedSnapshot is { } snap)
            Close(new OlvmSnapshotSelectResult(MssqlSnapshotAction.Restore, snap));
        else
            Close(null);
    }

    private void OnDrop(object? _, RoutedEventArgs e)
    {
        if (DataContext is OlvmSnapshotSelectViewModel vm && vm.SelectedSnapshot is { } snap)
            Close(new OlvmSnapshotSelectResult(MssqlSnapshotAction.Drop, snap));
        else
            Close(null);
    }
}
