using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace DTM.Data.Api;

/// <summary>
/// Einfachste tragfaehige Bearer-Auth: konstanter Vergleich gegen ein
/// statisches Token aus den Einstellungen. Kein JWT, keine Ablaufzeit —
/// Rotation heisst Token aendern und App neu starten.
///
/// <para>Bewusst kein ASP.NET-Auth-Handler: Schemes, Policies und
/// Registrierung waeren fuer eine Loopback-API deutlich mehr Apparat als
/// Nutzen.</para>
///
/// <para>Ohne gesetztes Token beantwortet die API <b>jeden</b> Request mit
/// 403. Das ist Absicht — eine offene Steuer-API auf einem Rechner mit
/// Datenbank-Zugaengen soll es nicht versehentlich geben.</para>
/// </summary>
internal static class ApiBearerAuth
{
    public static Task Enforce(HttpContext ctx, string? expectedToken, Func<Task> next)
    {
        if (string.IsNullOrWhiteSpace(expectedToken))
            return WriteProblem(ctx, StatusCodes.Status403Forbidden, "API ohne Token",
                "Es ist kein Bearer-Token gesetzt (settings.json → Api.BearerToken oder --api-token). "
                + "Ohne Token verweigert die API jeden Zugriff.");

        if (!ctx.Request.Headers.TryGetValue("Authorization", out var authHeader))
            return WriteProblem(ctx, StatusCodes.Status401Unauthorized, "Authorization fehlt",
                "Erwartet wird der Header 'Authorization: Bearer <token>'.");

        string header = authHeader.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal))
            return WriteProblem(ctx, StatusCodes.Status401Unauthorized, "Authorization unvollstaendig",
                "Erwartet wird der Header 'Authorization: Bearer <token>'.");

        string provided = header[prefix.Length..].Trim();
        if (!FixedTimeEquals(provided, expectedToken))
            return WriteProblem(ctx, StatusCodes.Status403Forbidden, "Token stimmt nicht",
                "Das uebergebene Bearer-Token passt nicht zur Konfiguration.");

        return next();
    }

    /// <summary>
    /// Vergleich in konstanter Zeit, damit sich das Token nicht zeichenweise
    /// ueber Antwortzeiten ableiten laesst. Bei Loopback-Traffic eher Prinzip
    /// als Notwendigkeit — kostet aber auch nichts.
    /// </summary>
    internal static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static Task WriteProblem(HttpContext ctx, int status, string title, string detail)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        var problem = new
        {
            type = "about:blank",
            title,
            status,
            detail,
            instance = ctx.Request.Path.Value,
        };
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
