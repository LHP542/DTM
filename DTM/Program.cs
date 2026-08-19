using Avalonia;
using DTM.Diagnostics;
using NLog;

namespace DTM;

internal static class Program
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Ergebnis des CLI-Parsers, von <see cref="App"/> beim Startup abgeholt.
    /// Statisch, weil der Avalonia-Lifecycle keine Stelle bietet, an der sich
    /// Argumente sauber in die App-Instanz reichen liessen.
    /// </summary>
    public static DTM.Config.AppLaunchOptions LaunchOptions { get; private set; } = new();

    [STAThread]
    public static int Main(string[] args)
    {
        // Telemetrie-Opt-Out MUSS gesetzt sein, BEVOR PowerShell-SDK-Typen JIT'd
        // oder Microsoft.ApplicationInsights initialisiert wird. Daher: erste Zeile.
        //   POWERSHELL_TELEMETRY_OPTOUT  - PowerShell stoppt eigene AppInsights-Aufrufe
        //   APPLICATIONINSIGHTS_NO_DIAGNOSTIC_CHANNEL - AI schreibt keine Trace-Envelopes mehr
        //   DOTNET_CLI_TELEMETRY_OPTOUT  - .NET CLI/Runtime-Telemetrie
        DisableThirdPartyTelemetry();

        // Catch-All fuer Exceptions abseits des UI-Threads. Den UI-Handler
        // registriert App.OnFrameworkInitializationCompleted, sobald der
        // Dispatcher existiert.
        FatalErrorHandler.Install();

        // CLI vor allem anderen auswerten — die REST-API-Optionen braucht die
        // App beim Startup, und Parser-Warnungen sollen im Log stehen, bevor
        // irgendetwas anderes passiert.
        LaunchOptions = DTM.Config.AppLaunchOptions.Parse(args);

        // Single-Instance-Guard VOR Avalonia: laeuft schon eine Instanz, wird
        // sie nach vorn geholt und dieser Prozess beendet sich, ohne dass ein
        // zweiter PowerShell-Runspace oder ein zweites Tray-Icon entsteht.
        var guard = new SingleInstanceGuard();
        if (!guard.TryClaim())
        {
            guard.NotifyPrimary();
            guard.Dispose();
            return 0;
        }

        // Uebergabe an die App, die den Guard verkabelt und beim Beenden
        // freigibt.
        App.PendingGuard = guard;

        try
        {
            // Nur die von DTM nicht verstandenen Argumente weiterreichen,
            // damit Avalonia seine eigenen Flags noch sieht.
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(LaunchOptions.RemainingArgs);
        }
        catch (Exception ex)
        {
            _logger.Fatal(ex, "Unbehandelter Fehler beim App-Start.");
            throw;
        }
    }

    private static void DisableThirdPartyTelemetry()
    {
        static void SetIfMissing(string key, string value)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }

        SetIfMissing("POWERSHELL_TELEMETRY_OPTOUT", "1");
        SetIfMissing("APPLICATIONINSIGHTS_NO_DIAGNOSTIC_CHANNEL", "1");
        SetIfMissing("DOTNET_CLI_TELEMETRY_OPTOUT", "1");
        SetIfMissing("DOTNET_TELEMETRY_OPTOUT", "1");
        // Optional: eigenes Distribution-Channel-Tag, damit man unsere Hosts in
        // möglichen Logs eindeutig identifizieren könnte (kein PII).
        SetIfMissing("POWERSHELL_DISTRIBUTION_CHANNEL", "DTM-Embedded");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
