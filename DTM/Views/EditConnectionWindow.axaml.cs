using Avalonia.Interactivity;

namespace DTM.Views;

public partial class EditConnectionWindow : ChromeWindow
{
    public EditConnectionWindow()
    {
        InitializeComponent();
    }

    private void OnTitleClose(object? _, RoutedEventArgs e) => Close(false);

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
