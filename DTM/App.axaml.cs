using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DTM.Composition;
using DTM.Config;
using DTM.Diagnostics;
using DTM.ViewModels;
using DTM.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DTM;

public partial class App : Application
{
    private static readonly NLog.ILogger _logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Composition-Root des laufenden Prozesses. Wird in <see cref="Initialize"/>
    /// gebaut. Tests instanziieren ViewModels weiterhin direkt und beruehren
    /// diesen Container nicht.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = default!;

    /// <summary>
    /// Wird von <see cref="Program.Main"/> gesetzt, sobald dieser Prozess die
    /// Erstinstanz ist. Die App uebernimmt ihn hier, verkabelt die Aktivierung
    /// und gibt ihn beim Beenden frei.
    /// </summary>
    public static SingleInstanceGuard? PendingGuard { get; set; }

    // GC-Referenz: OHNE Feld verschwindet das Tray-Icon nach einiger Laufzeit
    // (Skill-Standard, siehe TrayController-Klassenkommentar).
    private TrayController? _tray;
    private SingleInstanceGuard? _guard;
    private DTM.Data.Api.ApiHost? _api;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // FOC-SQL-Runtime hat ihren eigenen static-Singleton (FocSqlRuntime.Current);
        // bleibt aus dem DI-Container raus, damit der Lifecycle nicht zerfaellt.
        DTM.Data.Terminal.FocSqlRuntime.Current = AppSettingsStore.LoadFocSql();

        Services = new ServiceCollection()
            .AddDtmServices()
            .BuildServiceProvider();

        // Phase 9.5: Server-Liste an den TerminalBus geben, damit die
        // $global:DtmCredMap (PS-Remoting-Credentials pro Server) in den
        // Runspace injiziert wird, sobald das ConsoleControl attached.
        var servers = Services.GetRequiredService<IReadOnlyList<DB_SERVER>>();
        DTM.Data.Terminal.TerminalBus.SetCredMap(servers);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Dispatcher existiert erst jetzt — daher hier und nicht in Program.Main.
        FatalErrorHandler.InstallUiHandlers();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var main = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };
            desktop.MainWindow = main;

            // System-Tray: Minimieren → Tray, Schliessen → beendet regulaer.
            // Kein ShutdownMode-Umbau noetig (Hide statt Close beim Minimize).
            _tray = new TrayController(this, main);
            _tray.Install();

            // Zweitstart holt uns nach vorn. Das Event kommt vom ThreadPool,
            // deshalb der Sprung auf den UI-Thread. Restore() deckt auch den
            // Fall ab, dass das Fenster gerade im Tray versteckt ist.
            _guard = PendingGuard;
            PendingGuard = null;
            if (_guard is not null)
            {
                _guard.ActivationRequested += (_, _) =>
                    Dispatcher.UIThread.Post(() => _tray.Restore());

                // Pipe freigeben, damit ein direkt folgender Neustart sie
                // wieder belegen kann.
                desktop.Exit += (_, _) => _guard?.Dispose();
            }

            StartApiIfEnabled(desktop);
            ScheduleAutoShutdownIfRequested(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Startet die lokale REST-API, wenn sie in den Einstellungen oder per
    /// <c>--api-port</c> eingeschaltet ist. Der Start laeuft bewusst
    /// nebenlaeufig: das Fenster soll nicht auf Kestrel warten, und ein
    /// Fehlschlag darf DTM nicht aufhalten (der Host loggt und laeuft weiter).
    /// </summary>
    private void StartApiIfEnabled(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var settings = AppSettingsStore.LoadFocSql().Api;
        var options = DTM.Data.Api.ApiOptionsResolver.Resolve(settings, Program.LaunchOptions);
        if (!options.Enabled) return;

        _api = new DTM.Data.Api.ApiHost();
        _ = _api.StartAsync(options);

        // Kestrel sauber herunterfahren. Achtung: im Update-Pfad beendet sich
        // DTM per Process.Kill() — dann laeuft das hier nicht, was in Ordnung
        // ist, weil das Betriebssystem den Port ohnehin freigibt.
        desktop.Exit += (_, _) =>
        {
            try { _api?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)); }
            catch (Exception ex) { _logger.Warn(ex, "REST-API konnte nicht sauber gestoppt werden."); }
        };
    }

    /// <summary>
    /// Beendet die App nach <c>--auto-shutdown-after</c> von selbst. Gedacht
    /// fuer automatisierte Laeufe, damit keine Instanz stehen bleibt, wenn das
    /// steuernde Skript abbricht.
    /// </summary>
    private static void ScheduleAutoShutdownIfRequested(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (Program.LaunchOptions.AutoShutdownAfter is not { } delay) return;

        _logger.Info("Auto-Shutdown in {0:0} s (--auto-shutdown-after).", delay.TotalSeconds);
        _ = Task.Delay(delay).ContinueWith(_ =>
            Dispatcher.UIThread.Post(() =>
            {
                _logger.Info("Auto-Shutdown erreicht — DTM wird beendet.");
                desktop.Shutdown();
            }));
    }
}
