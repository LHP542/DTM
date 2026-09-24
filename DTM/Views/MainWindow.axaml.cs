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

    // Minimieren/Maximieren/Schließen und der Glyph-Wechsel beim Maximieren
    // liegen im TitleBar-Control. Hier bleibt nur der fensterspezifische
    // "Über"-Button, den die Titelleiste über ExtraContent einhängt.
    private async void OnAbout(object? _, RoutedEventArgs e) =>
        await new AboutWindow().ShowDialog(this);
}
