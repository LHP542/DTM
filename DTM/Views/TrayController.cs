using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NLog;

namespace DTM.Views;

/// <summary>
/// System-Tray-Integration nach Kroste-Skill-Standard (Referenz:
/// Checkmk Cockpit). Verhalten:
/// - <b>Minimieren</b> → Fenster verschwindet in den Tray (<see cref="Window.Hide"/>).
/// - <b>Schließen</b> → App beendet regulär (kein <c>ShutdownMode</c>-Umbau noetig).
/// - Klick aufs Tray-Icon oder Menue "Anzeigen" → Fenster kommt zurueck.
/// - Menue "Beenden" → sauberer Desktop-Shutdown.
///
/// Vier Fallen, die der Skill dokumentiert:
/// - <b>GC-Referenz halten</b>: <see cref="App"/> haelt die Instanz in einem
///   privaten Feld, sonst wird das <c>TrayIcon</c> nach einiger Laufzeit vom
///   GC eingesammelt und verschwindet "zufaellig".
/// - <b>Restore-Guard</b>: Setzen von <see cref="WindowState.Normal"/> triggert
///   den Listener rekursiv → ohne <see cref="_restoreInProgress"/>-Flag
///   entsteht eine Minimize/Restore-Schleife. Restore laeuft ausserdem ueber
///   <see cref="Dispatcher.UIThread.Post"/>, damit das Fenster nicht mitten im
///   Listener-Callback flackert.
/// - <b>Try/Catch mit Fallback</b>: Auf headless-Servern / kaputtem DBus ist
///   <c>TrayIcon.SetIcons</c> nicht verfuegbar — dann verhaelt sich Minimieren
///   normal (kein Hide), App bleibt nutzbar.
/// - Unter Linux haengt der Tray an <c>Tmds.DBus.Protocol</c>, das Avalonia
///   transitive mitzieht. Kein zusaetzliches Paket noetig.
/// </summary>
public sealed class TrayController
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    private readonly Application _app;
    private readonly Window _window;
    private TrayIcon? _tray;
    private bool _restoreInProgress;

    public TrayController(Application app, Window window)
    {
        _app = app;
        _window = window;
    }

    public void Install()
    {
        try
        {
            var iconUri = new Uri("avares://DTM/Assets/dtm.png");
            var icon = AssetLoader.Exists(iconUri)
                ? new WindowIcon(new Bitmap(AssetLoader.Open(iconUri)))
                : null;

            _tray = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "DTM — Datenbank-Manager",
                IsVisible = true,
                Menu = BuildMenu(),
            };
            // Linksklick aufs Tray-Icon → Fenster hervorholen.
            _tray.Clicked += (_, _) => Restore();

            TrayIcon.SetIcons(_app, new TrayIcons { _tray });

            // WindowState-Listener: Minimize → Hide (Tray).
            _window.PropertyChanged += OnWindowPropertyChanged;

            _logger.Info("System-Tray installiert (Minimize → Tray).");
        }
        catch (Exception ex)
        {
            // Fallback ohne Tray: Fenster minimiert sich normal in die
            // Taskleiste. App bleibt voll funktionsfaehig.
            _tray = null;
            _logger.Warn(ex, "System-Tray nicht verfuegbar — Fallback: Standard-Minimieren.");
        }
    }

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();

        var showItem = new NativeMenuItem("Anzeigen");
        showItem.Click += (_, _) => Restore();
        menu.Add(showItem);

        menu.Add(new NativeMenuItemSeparator());

        var quitItem = new NativeMenuItem("Beenden");
        quitItem.Click += (_, _) => Quit();
        menu.Add(quitItem);

        return menu;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty) return;
        if (_restoreInProgress) return;
        if (_window.WindowState != WindowState.Minimized) return;

        // Fenster verstecken statt in die Taskleiste zu minimieren. Hide()
        // schliesst nicht — der Prozess bleibt am Leben, die Session
        // (PowerShell-Runspace, DB-Verbindungen) bleibt bestehen.
        _window.Hide();
    }

    private void Restore()
    {
        // Ueber den Dispatcher gepostet, damit der Restore auch dann sauber
        // laeuft, wenn der Aufruf aus einem Nicht-UI-Kontext kommt (TrayIcon.
        // Clicked oder Menue-Klick auf manchen Plattformen).
        Dispatcher.UIThread.Post(() =>
        {
            _restoreInProgress = true;
            try
            {
                _window.Show();
                _window.WindowState = WindowState.Normal;
                _window.Activate();
            }
            finally
            {
                _restoreInProgress = false;
            }
        });
    }

    private void Quit()
    {
        if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
