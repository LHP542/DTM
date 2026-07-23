using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NLog;
using NLog.Config;
using NLog.LayoutRenderers;
using NLog.LayoutRenderers.Wrappers;

namespace DTM.Diagnostics;

/// <summary>
/// Wrapper-LayoutRenderer <c>${masked:inner=...}</c>: durchsucht die Inner-
/// Ausgabe nach Passwoertern/Tokens/Credentials und ersetzt den Wert durch
/// <c>***</c>. Harte Absicherung gegen versehentliches Log-Leak — nicht darauf
/// verlassen, dass jede Call-Site vorher maskiert. Register via [ModuleInitializer]
/// laeuft vor allen statischen Feld-Initialisierern im Modul (also insbesondere
/// vor <c>LogManager.GetCurrentClassLogger()</c> in Program.cs).
/// </summary>
[LayoutRenderer("masked")]
[ThreadAgnostic]
public sealed class MaskingLayoutRenderer : WrapperLayoutRendererBase
{
    // Capture-Group-basiert (Prefix wird als Gruppe 1 gecaptured und via
    // ${1}*** unveraendert zurueckgeschrieben) — behaelt Whitespace/Trennzeichen
    // zwischen Key und Wert, ersetzt nur den Wert. Sicherer als lookbehind mit
    // \s*, das an frueheren Positionen matcht und Leerzeichen mitfrisst.
    private static readonly (Regex Pattern, string Replacement)[] Rules =
    [
        // ConnectionString-Style: Password=xyz; PWD=xyz;
        // (&/,/} auch ausgeschlossen, damit URL-Query/JSON-Kontexte nicht
        // ueber die Wortgrenze mitgefressen werden.)
        (new(@"(?i)(Password|PWD)(\s*=\s*)[^;""'\s&,}]+",
             RegexOptions.Compiled), "$1$2***"),

        // URL-/JSON-Style: password=xyz, token=xyz, api_key=xyz, apikey=xyz
        (new(@"(?i)\b(password|token|api[_-]?key)(\s*[=:]\s*)[""']?[^&;""'\s,}]+[""']?",
             RegexOptions.Compiled), "$1$2***"),

        // Bearer <token>
        (new(@"(?i)(Bearer)(\s+)[A-Za-z0-9._\-]+",
             RegexOptions.Compiled), "$1$2***"),

        // Authorization: <wert>
        (new(@"(?i)(Authorization)(\s*:\s*)[^\r\n]+",
             RegexOptions.Compiled), "$1$2***"),
    ];

    [ModuleInitializer]
    internal static void Register()
    {
        // NLog v5.2+: SetupBuilder-API statt der veralteten
        // LayoutRenderer.Register<T>. Additive Registrierung ueber
        // ConfigurationItemFactory — laesst die per Nlog.config geladene
        // Konfiguration unangetastet.
        LogManager.Setup().SetupExtensions(ext =>
            ext.RegisterLayoutRenderer<MaskingLayoutRenderer>("masked"));
    }

    protected override string Transform(string text) => Mask(text);

    /// <summary>Public entry point fuer Tests und direkte Verwendung.</summary>
    public static string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        foreach (var (pattern, replacement) in Rules)
            text = pattern.Replace(text, replacement);
        return text;
    }
}
