using Avalonia.Interactivity;

namespace DTM.Views;

/// <summary>
/// Dialog zur Auswahl eines Oracle-Restore-Points vor dem eigentlichen
/// <c>Restore-Snapshot</c>-Aufruf. Result: <c>true</c> = User hat „Restore
/// ausführen" geklickt (mit gültiger Auswahl); <c>false</c> = abgebrochen.
/// </summary>
public partial class OracleRestoreSelectWindow : ChromeWindow
{
    public OracleRestoreSelectWindow()
    {
        InitializeComponent();
        // Klick auf "X" = abgebrochen, kein Restore auslösen.
        Bar.CloseResult = false;
    }

    private void OnCancel(object? _, RoutedEventArgs e) => Close(false);
    private void OnRestore(object? _, RoutedEventArgs e) => Close(true);
}
