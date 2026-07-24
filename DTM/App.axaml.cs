using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

    // GC-Referenz: OHNE Feld verschwindet das Tray-Icon nach einiger Laufzeit
    // (Skill-Standard, siehe TrayController-Klassenkommentar).
    private TrayController? _tray;

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
        }

        base.OnFrameworkInitializationCompleted();
    }
}
