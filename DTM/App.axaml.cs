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
        }

        base.OnFrameworkInitializationCompleted();
    }
}
