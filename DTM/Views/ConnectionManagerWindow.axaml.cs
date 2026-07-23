using Avalonia.Interactivity;
using DTM.ViewModels;

namespace DTM.Views;

public partial class ConnectionManagerWindow : ChromeWindow
{
    public ConnectionManagerWindow()
    {
        InitializeComponent();
    }

    private ConnectionManagerViewModel Vm => (ConnectionManagerViewModel)DataContext!;

    private async void OnAdd(object? sender, RoutedEventArgs e)
    {
        EditConnectionViewModel editVm = new();
        EditConnectionWindow dlg = new() { DataContext = editVm };
        bool ok = await dlg.ShowDialog<bool>(this);
        if (ok) Vm.AddEntry(editVm.ToEntry());
    }

    private async void OnEdit(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedConnection is null) return;
        EditConnectionViewModel editVm = new(Vm.SelectedConnection);
        EditConnectionWindow dlg = new() { DataContext = editVm };
        bool ok = await dlg.ShowDialog<bool>(this);
        if (ok) Vm.UpdateEntry(editVm.ToEntry());
    }

    private void OnDelete(object? sender, RoutedEventArgs e) => Vm.DeleteSelected();

    private void OnSaveFocSql(object? sender, RoutedEventArgs e) => Vm.SaveFocSql();
}
