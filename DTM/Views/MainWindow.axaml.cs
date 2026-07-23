using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DTM.ViewModels;

namespace DTM.Views;

public partial class MainWindow : ChromeWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.CheckForUpdateAsync();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "☐";
    }

    private void OnMinimize(object? _, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximize(object? _, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnClose(object? _, RoutedEventArgs e) => Close();

    private async void OnAbout(object? _, RoutedEventArgs e) =>
        await new AboutWindow().ShowDialog(this);
}
