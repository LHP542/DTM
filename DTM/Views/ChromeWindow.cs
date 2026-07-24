using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

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
    private static readonly Uri IconUri = new("avares://DTM/Assets/dtm.png");

    public ChromeWindow()
    {
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        CanResize = true;

        // App-Icon fuer Fenster (Taskbar/Alt-Tab/Wayland-Header). Try/Catch:
        // fehlt das Asset (z.B. beim Design-Runtime), lauft die App
        // weiter — nur ohne Icon.
        try
        {
            if (AssetLoader.Exists(IconUri))
                Icon = new WindowIcon(new Bitmap(AssetLoader.Open(IconUri)));
        }
        catch
        {
            // Icon ist Kosmetik — nicht kritisch fuer die App-Funktion.
        }
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
