using Avalonia.Interactivity;

namespace DTM.Views;

public partial class EditConnectionWindow : ChromeWindow
{
    public EditConnectionWindow()
    {
        InitializeComponent();
        // Klick auf "X" = Abbrechen, nicht speichern.
        Bar.CloseResult = false;
    }


    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
