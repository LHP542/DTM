using Avalonia.Interactivity;
using DTM.ViewModels;

namespace DTM.Views;

public partial class SessionsWindow : ChromeWindow
{
    public SessionsWindow()
    {
        InitializeComponent();
    }

    private void OnTitleClose(object? _, RoutedEventArgs e) => Close();

    private async void OnCloseAllSessions(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SessionsViewModel vm || !vm.CanCloseSessions) return;

        ConfirmWindow dlg = new()
        {
            WindowTitle = "Sessions beenden?",
            Message = $"Alle aktiven Sessions zur Datenbank „{vm.DatabaseDisplayName}\" werden "
                    + "per KILL beendet. Laufende Transaktionen werden rolled back — nicht "
                    + "gespeicherte Daten in verbundenen Anwendungen gehen verloren.\n\n"
                    + "Wirklich fortfahren?",
            ConfirmText = "Beenden",
            CancelText = "Abbrechen",
        };

        bool ok = await dlg.ShowDialog<bool>(this);
        if (!ok) return;

        vm.PerformCloseAllSessions();
        // Fenster schliessen — Sessions-Liste wird beim naechsten DB-Select neu geladen.
        Close();
    }
}
