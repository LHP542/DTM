using Avalonia.Controls;
using Avalonia.Input;

namespace DTM.Views;

/// <summary>
/// Basisklasse für alle DTM-Fenster mit Custom-Chrome (Avalonia 12).
/// Setzt die einheitliche Fenster-Dekoration und stellt die gemeinsamen
/// Drag/DoubleTap-Handler für die selbstgebaute Titelleiste bereit.
/// Window-spezifische Logik (Min/Max-Glyph, Schließen mit Dialog-Result)
/// bleibt in der jeweiligen abgeleiteten Klasse.
/// </summary>
public class ChromeWindow : Window
{
    public ChromeWindow()
    {
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        CanResize = true;
    }

    public void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    public void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
}
