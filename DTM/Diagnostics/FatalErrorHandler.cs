using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using DTM.Views;
using NLog;

namespace DTM.Diagnostics;

/// <summary>
/// Zentraler Catch-All fuer unbehandelte Exceptions:
/// AppDomain (Hintergrund-Thread, terminiert Prozess), TaskScheduler
/// (vergessene async-Pfade, GC findet sie spaeter), Dispatcher (UI-Thread).
///
/// AppDomain-Exceptions beenden den Prozess sowieso — dort nur loggen.
/// TaskScheduler/Dispatcher koennen ueberlebt werden — dort zusaetzlich
/// einen freundlichen FatalErrorWindow-Dialog anzeigen.
/// </summary>
internal static class FatalErrorHandler
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private static int _dialogOpen;

    /// <summary>
    /// Registriert die Plattform-/Runtime-Handler. Muss vor dem App-Start
    /// in <c>Program.Main</c> aufgerufen werden.
    /// </summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// Registriert den Avalonia-UI-Handler. Muss nach Avalonia-Init laufen
    /// (typischerweise in <c>App.OnFrameworkInitializationCompleted</c>),
    /// weil <see cref="Dispatcher.UIThread"/> davor noch nicht existiert.
    /// </summary>
    public static void InstallUiHandlers()
    {
        Dispatcher.UIThread.UnhandledException += OnDispatcherException;
    }

    private static void OnAppDomainException(object? _, UnhandledExceptionEventArgs e)
    {
        Exception ex = e.ExceptionObject as Exception
                       ?? new Exception($"Unbekanntes Exception-Objekt: {e.ExceptionObject}");
        _logger.Fatal(ex, "Unbehandelte AppDomain-Exception (IsTerminating={0})", e.IsTerminating);
        // Kein Dialog: Prozess geht ohnehin zu Ende, das UI ist nicht mehr garantiert lebendig.
    }

    private static void OnUnobservedTaskException(object? _, UnobservedTaskExceptionEventArgs e)
    {
        _logger.Fatal(e.Exception, "Unbeobachtete Task-Exception");
        e.SetObserved();
        TryShowDialog(e.Exception, "Hintergrund-Task");
    }

    private static void OnDispatcherException(object? _, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.Fatal(e.Exception, "Unbehandelte UI-Dispatcher-Exception");
        e.Handled = true;
        TryShowDialog(e.Exception, "UI-Thread");
    }

    private static void TryShowDialog(Exception ex, string source)
    {
        // Single-Dialog-Guard: bei Exception-Sturm nicht ein Dutzend Fenster aufmachen.
        if (Interlocked.CompareExchange(ref _dialogOpen, 1, 0) != 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                FatalErrorWindow dlg = new(ex, source);
                dlg.Closed += (_, _) => Interlocked.Exchange(ref _dialogOpen, 0);

                Window? owner = GetOwnerWindow();
                if (owner is not null && owner.IsVisible)
                    _ = dlg.ShowDialog(owner);
                else
                    dlg.Show();
            }
            catch (Exception dialogEx)
            {
                // Wenn schon das UI hochkommt, nicht in eine Endlosschleife krachen.
                Interlocked.Exchange(ref _dialogOpen, 0);
                _logger.Error(dialogEx, "FatalErrorWindow konnte nicht angezeigt werden.");
            }
        });
    }

    private static Window? GetOwnerWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
