using System.Globalization;
using NLog;

namespace DTM.Config;

/// <summary>
/// Ergebnis des CLI-Parsers. Nur die von DTM verstandenen Argumente landen
/// hier; alles andere geht als <see cref="RemainingArgs"/> weiter an
/// <c>StartWithClassicDesktopLifetime</c>, damit Avalonia seine eigenen Flags
/// noch sieht.
///
/// <para>Unterstuetzt:
/// <c>--api-port &lt;n&gt;</c>, <c>--api-token &lt;s&gt;</c>,
/// <c>--api-allow-destructive</c>, <c>--auto-shutdown-after &lt;dauer&gt;</c>.</para>
/// </summary>
public sealed class AppLaunchOptions
{
    /// <summary>Setzt den API-Port und schaltet die API implizit ein.</summary>
    public int? ApiPortOverride { get; init; }

    public string? ApiTokenOverride { get; init; }

    /// <summary><c>true</c> nur wenn das Flag explizit gesetzt wurde;
    /// <c>null</c> = keine Aussage, dann zaehlt die Einstellung.</summary>
    public bool? ApiAllowDestructiveOverride { get; init; }

    /// <summary>Beendet die App nach dieser Zeit von selbst. Gedacht fuer
    /// automatisierte Laeufe, damit keine Instanz stehen bleibt.</summary>
    public TimeSpan? AutoShutdownAfter { get; init; }

    public string[] RemainingArgs { get; init; } = [];

    public static AppLaunchOptions Parse(string[] args)
    {
        ILogger log = LogManager.GetCurrentClassLogger();
        int? port = null;
        string? token = null;
        bool? allowDestructive = null;
        TimeSpan? shutdown = null;
        List<string> remaining = [];

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--api-port":
                    if (i + 1 < args.Length
                        && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int p)
                        && p is > 0 and < 65536)
                    {
                        port = p;
                        i++;
                    }
                    else
                    {
                        log.Warn("--api-port ohne gueltigen Wert (1-65535) — ignoriert.");
                    }
                    break;

                case "--api-token":
                    if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        token = args[i + 1];
                        i++;
                    }
                    else
                    {
                        log.Warn("--api-token ohne Wert — ignoriert.");
                    }
                    break;

                case "--api-allow-destructive":
                    allowDestructive = true;
                    break;

                case "--auto-shutdown-after":
                    if (i + 1 < args.Length && TryParseDuration(args[i + 1], out TimeSpan d))
                    {
                        shutdown = d;
                        i++;
                    }
                    else
                    {
                        log.Warn("--auto-shutdown-after ohne gueltigen Wert (z.B. 30s, 5m, 1h) — ignoriert.");
                    }
                    break;

                default:
                    remaining.Add(args[i]);
                    break;
            }
        }

        return new AppLaunchOptions
        {
            ApiPortOverride = port,
            ApiTokenOverride = token,
            ApiAllowDestructiveOverride = allowDestructive,
            AutoShutdownAfter = shutdown,
            RemainingArgs = [.. remaining],
        };
    }

    /// <summary>Akzeptiert "30s", "5m", "1h" und blanke Sekundenzahlen.</summary>
    internal static bool TryParseDuration(string value, out TimeSpan result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        string v = value.Trim().ToLowerInvariant();
        char unit = v[^1];
        string number = char.IsDigit(unit) ? v : v[..^1];

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || n <= 0)
            return false;

        result = unit switch
        {
            's' => TimeSpan.FromSeconds(n),
            'm' => TimeSpan.FromMinutes(n),
            'h' => TimeSpan.FromHours(n),
            _ when char.IsDigit(unit) => TimeSpan.FromSeconds(n),
            _ => TimeSpan.Zero,
        };
        return result > TimeSpan.Zero;
    }
}
