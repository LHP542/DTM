using DTM.Config;

namespace DTM.Data.Api;

/// <summary>
/// Effektive API-Konfiguration nach dem Zusammenfuehren von settings.json und
/// CLI-Argumenten. <see cref="Enabled"/> ist true, wenn entweder ein
/// CLI-Port gesetzt wurde ODER <c>Api.Enabled</c> in den Einstellungen steht.
/// </summary>
/// <param name="Enabled">API ueberhaupt starten.</param>
/// <param name="Port">Loopback-Port.</param>
/// <param name="BearerToken">Erwartetes Token; leer = jeder Request 403.</param>
/// <param name="AllowDestructive">Erlaubt Commands, die Datenbanken veraendern.</param>
public sealed record ApiOptions(bool Enabled, int Port, string? BearerToken, bool AllowDestructive);

/// <summary>
/// Fuehrt <see cref="ApiSettings"/> und <see cref="AppLaunchOptions"/> zusammen.
/// CLI gewinnt gegen die Einstellungen — so laesst sich ein automatisierter Lauf
/// starten, ohne die persistente Konfiguration des Nutzers anzufassen.
/// </summary>
public static class ApiOptionsResolver
{
    public static ApiOptions Resolve(ApiSettings settings, AppLaunchOptions cli)
    {
        int port = cli.ApiPortOverride ?? settings.Port;
        string? token = cli.ApiTokenOverride ?? settings.BearerToken;

        // Ein CLI-Port schaltet die API implizit ein. Sonst muesste man beim
        // Automatisieren immer zusaetzlich die settings.json anfassen — und
        // genau das soll der CLI-Weg vermeiden.
        bool enabled = cli.ApiPortOverride is not null || settings.Enabled;

        // Destruktiv laesst sich nur zuschalten, nie per CLI abschalten:
        // Wer es in den Einstellungen erlaubt hat, hat das bewusst getan.
        bool destructive = cli.ApiAllowDestructiveOverride ?? settings.AllowDestructive;

        return new ApiOptions(enabled, port, token, destructive);
    }
}
