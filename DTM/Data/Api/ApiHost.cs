using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;

namespace DTM.Data.Api;

/// <summary>
/// Kestrel im selben Prozess, neben der Avalonia-App. Antwortet auf
/// <c>http://127.0.0.1:&lt;port&gt;</c>; alle UI-Zugriffe laufen ueber
/// <see cref="DtmUiActions"/> auf dem UI-Thread.
///
/// <para>Startet nur, wenn <see cref="ApiOptions.Enabled"/> gesetzt ist.
/// Schlaegt der Start fehl (Port belegt o.ae.), laeuft DTM ohne API weiter —
/// eine nicht startende Nebenfunktion darf das Werkzeug nicht blockieren.</para>
///
/// <para><b>Bindet ausschliesslich an Loopback.</b> Wer von einem anderen
/// Rechner zugreifen will, nimmt einen SSH-Tunnel — die Bind-Adresse bleibt,
/// wo sie ist.</para>
/// </summary>
public sealed class ApiHost : IAsyncDisposable
{
    // Voll qualifiziert: "ILogger" ist hier mehrdeutig, weil
    // Microsoft.Extensions.Logging fuer die Kestrel-Konfiguration im Scope ist.
    private static readonly NLog.ILogger _logger = LogManager.GetCurrentClassLogger();

    private WebApplication? _webApp;

    public bool IsRunning => _webApp is not null;

    public async Task StartAsync(ApiOptions options)
    {
        if (!options.Enabled)
        {
            _logger.Debug("REST-API deaktiviert (Api.Enabled=false und kein --api-port).");
            return;
        }
        if (_webApp is not null)
        {
            _logger.Warn("ApiHost laeuft bereits.");
            return;
        }

        try
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

            // Kestrel-eigene Logs raus — der Lifecycle wird ueber NLog
            // protokolliert, alles andere waere doppelt.
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);

            // CreateSlimBuilder liefert keinen Zugriff auf ConfigureKestrel;
            // der Weg ueber das Options-Pattern wirkt beim Server-Start.
            builder.Services.Configure<KestrelServerOptions>(k =>
            {
                k.ListenLocalhost(options.Port, listen => listen.Protocols = HttpProtocols.Http1);
                k.AddServerHeader = false;
            });

            WebApplication app = builder.Build();

            app.Use(async (ctx, next) => await ApiBearerAuth.Enforce(ctx, options.BearerToken, next));
            ApiEndpoints.MapAll(app, options);

            await app.StartAsync();
            _webApp = app;

            _logger.Info(
                "REST-API laeuft auf http://127.0.0.1:{0} (Token {1}, destruktive Aktionen {2})",
                options.Port,
                string.IsNullOrWhiteSpace(options.BearerToken) ? "FEHLT — alle Requests 403" : "gesetzt",
                options.AllowDestructive ? "ERLAUBT" : "gesperrt");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Start der REST-API fehlgeschlagen — DTM laeuft ohne API weiter.");
            _webApp = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_webApp is null) return;
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
            await _webApp.StopAsync(cts.Token);
            await _webApp.DisposeAsync();
            _logger.Info("REST-API gestoppt.");
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Fehler beim Stoppen der REST-API.");
        }
        finally
        {
            _webApp = null;
        }
    }
}
