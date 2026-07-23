using Avalonia;
using DTM.Diagnostics;
using NLog;

namespace DTM;

internal static class Program
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    [STAThread]
    public static void Main(string[] args)
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

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
