using Avalonia.Interactivity;
using DTM.ViewModels;

namespace DTM.Views;

public partial class TimePickerWindow : ChromeWindow
{
    public TimePickerWindow()
    {
        InitializeComponent();
        // Hier ZWINGEND explizit: der Dialog liefert ein Objekt, nicht bool.
        // Ohne CloseResult kaeme beim Klick auf "X" null zurueck, und
        // RunDbActionAsync wuerde beim Zugriff auf pick.Cancelled knallen —
        // statt die Aktion einfach abzubrechen.
        Bar.CloseResult = TimePickResult.Cancel();
    }


    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var dt = (DataContext as TimePickerViewModel)?.ComposeDateTime() ?? DateTime.Now;
        Close(TimePickResult.At(dt));
    }

    private void OnImmediate(object? sender, RoutedEventArgs e) => Close(TimePickResult.Immediate());

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(TimePickResult.Cancel());
}
