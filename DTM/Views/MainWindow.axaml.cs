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

    // Minimieren/Maximieren/Schliessen und der Glyph-Wechsel beim Maximieren
    // liegen im TitleBar-Control. Hier bleibt nur der fensterspezifische
    // "Ueber"-Button, den die Titelleiste ueber ExtraContent einhaengt.
    private async void OnAbout(object? _, RoutedEventArgs e) =>
        await new AboutWindow().ShowDialog(this);
}
