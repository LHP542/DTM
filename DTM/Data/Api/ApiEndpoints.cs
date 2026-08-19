using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using DTM.ViewModels;
using DTM.ViewModels.TreeNodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace DTM.Data.Api;

/// <summary>
/// Alle HTTP-Routen der DTM-API. Getrennt vom <see cref="ApiHost"/>-Lifecycle,
/// damit sie in Tests direkt gegen eine <see cref="WebApplication"/> gemountet
/// werden koennen.
///
/// <para>Alle Endpoints laufen hinter <see cref="ApiBearerAuth"/> — hier gibt
/// es keine eigenen Auth-Pruefungen mehr.</para>
/// </summary>
internal static class ApiEndpoints
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public const string ApiVersion = "1.0.0";

    public static void MapAll(WebApplication app, ApiOptions options)
    {
        DtmUiActions ui = new(options.AllowDestructive);

        // --- Zustand und Erkundung -------------------------------------

        app.MapGet("/state", async () =>
        {
            DtmUiActions.StateSnapshot s = await ui.GetStateAsync();

            var state = new
            {
                apiVersion = ApiVersion,
                appVersion = typeof(ApiEndpoints).Assembly
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                    .FirstOrDefault()?.InformationalVersion,
                allowDestructive = options.AllowDestructive,
                selectedNode = s.SelectedNode,
                statusBar = s.StatusBar,
                database = new
                {
                    name = s.DbName,
                    host = s.DbHost,
                    status = s.DbStatus,
                    version = s.DbVersion,
                    size = s.DbSize,
                    recovery = s.Recovery,
                    activeSessions = s.ActiveSessions,
                },
                openWindows = s.OpenWindows,
                mainWindow = s.Width is null ? null : new
                {
                    width = s.Width,
                    height = s.Height,
                    isMaximized = s.IsMaximized,
                },
            };
            return Results.Text(JsonSerializer.Serialize(state, Json), "application/json");
        });

        app.MapGet("/tree", async () =>
        {
            IReadOnlyList<DtmUiActions.ServerGroupSnapshot>? tree = await ui.GetTreeAsync();
            if (tree is null)
                return Problem(StatusCodes.Status503ServiceUnavailable, "Noch nicht bereit",
                    "Das MainWindow-ViewModel ist noch nicht initialisiert.");

            return Results.Text(JsonSerializer.Serialize(tree, Json), "application/json");
        });

        app.MapGet("/elements", async () =>
        {
            IReadOnlyList<string> names = await ui.ListElementsAsync();
            return Results.Text(JsonSerializer.Serialize(new { elementIds = names }, Json), "application/json");
        });

        // --- Navigation und Eingaben -----------------------------------

        app.MapPost("/select-node", async (HttpContext ctx) =>
        {
            SelectNodeRequest? body = await ReadJson<SelectNodeRequest>(ctx);
            if (body is null || string.IsNullOrWhiteSpace(body.Path))
                return Problem(StatusCodes.Status400BadRequest, "path fehlt",
                    "Body erwartet: { \"path\": \"<Server>\" } oder { \"path\": \"<Server>/<Datenbank>\" }");

            return Translate(await ui.SelectNodeAsync(body.Path), ctx);
        });

        app.MapPost("/click", async (HttpContext ctx) =>
        {
            ClickRequest? body = await ReadJson<ClickRequest>(ctx);
            if (body is null || string.IsNullOrWhiteSpace(body.ElementId))
                return Problem(StatusCodes.Status400BadRequest, "elementId fehlt",
                    "Body erwartet: { \"elementId\": \"…\" } — verfuegbare Namen liefert GET /elements.");

            return Translate(await ui.ClickAsync(body.ElementId), ctx);
        });

        app.MapPost("/command", async (HttpContext ctx) =>
        {
            CommandRequest? body = await ReadJson<CommandRequest>(ctx);
            if (body is null || string.IsNullOrWhiteSpace(body.Name))
                return Problem(StatusCodes.Status400BadRequest, "name fehlt",
                    "Body erwartet: { \"name\": \"ManageConnections\" } — Suffix 'Command' optional.");

            return Translate(await ui.ExecuteCommandAsync(body.Name), ctx);
        });

        app.MapPost("/text", async (HttpContext ctx) =>
        {
            TextRequest? body = await ReadJson<TextRequest>(ctx);
            if (body is null || string.IsNullOrWhiteSpace(body.ElementId))
                return Problem(StatusCodes.Status400BadRequest, "elementId fehlt",
                    "Body erwartet: { \"elementId\": \"…\", \"text\": \"…\" }");

            return Translate(await ui.SetTextAsync(body.ElementId, body.Text ?? string.Empty), ctx);
        });

        // --- Screenshot -------------------------------------------------

        app.MapPost("/screenshot", async (string? target, string? format) =>
        {
            byte[]? png = await ui.ScreenshotAsync(target ?? "main");
            if (png is null || png.Length == 0)
                return Problem(StatusCodes.Status409Conflict, "Kein zeichenbares Fenster",
                    "Es ist kein Fenster offen oder es hat die Groesse 0 (App startet vielleicht noch).");

            if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new
                {
                    format = "png",
                    pngBase64 = Convert.ToBase64String(png),
                    capturedAt = DateTimeOffset.UtcNow,
                }, contentType: "application/json");
            }
            return Results.File(png, "image/png");
        });
    }

    private static async Task<T?> ReadJson<T>(HttpContext ctx)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(ctx.Request.Body, Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>Uebersetzt ein <see cref="DtmUiActions.ActionResult"/> in eine HTTP-Antwort.</summary>
    private static IResult Translate(DtmUiActions.ActionResult res, HttpContext ctx)
    {
        if (res.Success) return Results.NoContent();

        return res.Failure switch
        {
            DtmUiActions.ActionFailure.NotFound => Results.Json(new
            {
                type = "about:blank",
                title = res.Error,
                status = 404,
                detail = "Nicht im aktuellen Zustand gefunden.",
                available = res.Available,
                instance = ctx.Request.Path.Value,
            }, contentType: "application/problem+json", statusCode: StatusCodes.Status404NotFound),

            // 403 statt 409: die Sperre ist eine Berechtigungsfrage, kein
            // Zustandsproblem — ein Retry hilft nicht, nur Umkonfigurieren.
            DtmUiActions.ActionFailure.Blocked =>
                Problem(StatusCodes.Status403Forbidden, "Destruktive Aktion gesperrt", res.Error!),

            DtmUiActions.ActionFailure.Unsupported =>
                Problem(StatusCodes.Status422UnprocessableEntity, "Aktion nicht unterstuetzt", res.Error!),

            _ => Problem(StatusCodes.Status409Conflict, "Zustand passt nicht", res.Error!),
        };
    }

    private static IResult Problem(int status, string title, string detail) =>
        Results.Json(new { type = "about:blank", title, status, detail },
            contentType: "application/problem+json", statusCode: status);

    private sealed record SelectNodeRequest(string? Path);
    private sealed record ClickRequest(string? ElementId);
    private sealed record CommandRequest(string? Name);
    private sealed record TextRequest(string? ElementId, string? Text);
}
