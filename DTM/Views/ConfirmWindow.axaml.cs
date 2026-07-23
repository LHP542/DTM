using Avalonia.Interactivity;

namespace DTM.Views;

/// <summary>
/// Generischer Confirm-Dialog im DTM-Stil. Result: <c>true</c> = bestätigt,
/// <c>false</c> = abgebrochen (auch ueber X / Esc).
///
/// Beispiel:
///   var dlg = new ConfirmWindow {
///       WindowTitle = "Sessions beenden?",
///       Message = "Alle Sessions zu 'X' werden beendet.",
///       ConfirmText = "Beenden",
///   };
///   bool ok = await dlg.ShowDialog&lt;bool&gt;(owner);
/// </summary>
public partial class ConfirmWindow : ChromeWindow
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    /// <summary>Titel der Titelleiste.</summary>
    public string WindowTitle
    {
        get => TitleText.Text ?? string.Empty;
        set { TitleText.Text = value; Title = value; }
    }

    /// <summary>Fragetext (Hauptinhalt).</summary>
    public string Message
    {
        get => MessageText.Text ?? string.Empty;
        set => MessageText.Text = value;
    }

    /// <summary>Label des Confirm-Buttons (Default: „Bestätigen").</summary>
    public string ConfirmText
    {
        get => ConfirmButton.Content as string ?? string.Empty;
        set => ConfirmButton.Content = value;
    }

    /// <summary>Label des Cancel-Buttons (Default: „Abbrechen").</summary>
    public string CancelText
    {
        get => CancelButton.Content as string ?? string.Empty;
        set => CancelButton.Content = value;
    }

    private void OnTitleClose(object? _, RoutedEventArgs e) => Close(false);
    private void OnCancel(object? _, RoutedEventArgs e) => Close(false);
    private void OnConfirm(object? _, RoutedEventArgs e) => Close(true);
}
