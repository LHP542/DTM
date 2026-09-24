using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DTM.Views;

/// <summary>
/// Basisklasse für alle DTM-Fenster mit Custom-Chrome (Avalonia 12).
/// Setzt die einheitliche Fenster-Dekoration und das App-Icon.
///
/// Drag, Doppelklick-Maximieren, Min/Max/Close und der Glyph-Wechsel beim
/// Maximieren liegen seit dem TitleBar-Rollout komplett im
/// <see cref="Controls.TitleBar"/>-Control — jedes Fenster bindet es als
/// <c>&lt;c:TitleBar/&gt;</c> ein, statt die Leiste selbst zu bauen.
/// Fensterspezifische Chrome-Buttons kommen über
/// <c>TitleBar.ExtraContent</c> dazu (MainWindow: „Über"-Button).
/// </summary>
public class ChromeWindow : Window
{
    private static readonly Uri IconUri = new("avares://DTM/Assets/dtm.png");

    public ChromeWindow()
    {
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        // Kanon-Wert aus der Skill-Vorlage: -1 überlässt die gesamte Höhe der
        // eigenen Titelleiste, statt oben einen Streifen für die
        // System-Caption zu reservieren.
        ExtendClientAreaTitleBarHeightHint = -1;
        CanResize = true;

        // App-Icon für Fenster (Taskbar/Alt-Tab/Wayland-Header). Try/Catch:
        // fehlt das Asset (z.B. beim Design-Runtime), lauft die App
        // weiter — nur ohne Icon.
        try
        {
            if (AssetLoader.Exists(IconUri))
                Icon = new WindowIcon(new Bitmap(AssetLoader.Open(IconUri)));
        }
        catch
        {
            // Icon ist Kosmetik — nicht kritisch für die App-Funktion.
        }
    }

}
