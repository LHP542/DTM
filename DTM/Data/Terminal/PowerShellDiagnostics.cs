using System.Management.Automation;
using System.Text;

namespace DTM.Data.Terminal;

/// <summary>
/// Helfer fuer In-Process-PowerShell-Aufrufe — formatiert die Fehler-,
/// Warnung- und Info-Streams zu einer aussagekraeftigen Diagnose-Nachricht,
/// damit ein <see cref="InvalidOperationException"/> nicht nur „… fehlgeschlagen"
/// sagt, sondern auch den ScriptName/Linenumber/FullyQualifiedErrorId der
/// urspruenglichen PowerShell-Fehler enthaelt.
/// </summary>
internal static class PowerShellDiagnostics
{
    public static void ThrowIfErrors(PowerShell ps, string stage)
    {
        if (!ps.HadErrors) return;
        throw new InvalidOperationException(FormatDiagnostics(ps, stage));
    }

    /// <summary>
    /// Formatiert den aktuellen Stream-Stand zu einer Diagnose-Nachricht.
    /// Trennt Errors, Warnungen und Information-Stream-Eintraege.
    /// </summary>
    public static string FormatDiagnostics(PowerShell ps, string stage)
    {
        StringBuilder sb = new();
        sb.Append(stage);

        if (ps.Streams.Error.Count > 0)
        {
            sb.Append(" — Fehler:");
            foreach (ErrorRecord err in ps.Streams.Error)
                AppendErrorRecord(sb, err);
        }
        else
        {
            sb.Append(" (Streams.Error leer)");
        }

        if (ps.Streams.Warning.Count > 0)
        {
            sb.Append("\nWarnungen:");
            foreach (WarningRecord w in ps.Streams.Warning)
                sb.Append("\n  - ").Append(w.Message);
        }

        // Information-Stream (Write-Host / Write-Information) kann zusaetzlich
        // Kontext liefern, der ohne Fehler-Position waere — z.B. „Baue
        // Verbindung zum Server X auf" direkt vor dem Crash.
        if (ps.Streams.Information.Count > 0)
        {
            int lastN = Math.Min(ps.Streams.Information.Count, 5);
            sb.Append("\nLetzte Information-Stream-Eintraege (max ").Append(lastN).Append("):");
            for (int i = ps.Streams.Information.Count - lastN; i < ps.Streams.Information.Count; i++)
                sb.Append("\n  - ").Append(ps.Streams.Information[i].MessageData?.ToString() ?? "");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Prueft, ob ein Cmdlet im aktuellen Runspace bekannt ist. Macht eine
    /// kurze Get-Command-Abfrage. State des PowerShell-Objekts (Commands/Streams)
    /// wird vorher gesichert und nachher wiederhergestellt — bzw. die
    /// Streams werden geleert.
    /// </summary>
    public static bool CommandExists(PowerShell ps, string commandName)
    {
        ps.Commands.Clear();
        ps.Streams.Error.Clear();

        ps.AddCommand("Get-Command")
          .AddParameter("Name", commandName)
          .AddParameter("ErrorAction", "SilentlyContinue");

        var result = ps.Invoke();

        ps.Commands.Clear();
        ps.Streams.Error.Clear();

        return result.Count > 0 && result[0] is not null;
    }

    private static void AppendErrorRecord(StringBuilder sb, ErrorRecord err)
    {
        sb.Append("\n  - ");
        string msg = err.Exception?.Message ?? err.ToString();
        sb.Append(msg);

        if (!string.IsNullOrEmpty(err.FullyQualifiedErrorId))
            sb.Append(" [").Append(err.FullyQualifiedErrorId).Append(']');

        InvocationInfo? inv = err.InvocationInfo;
        if (inv is not null)
        {
            if (!string.IsNullOrEmpty(inv.ScriptName))
                sb.Append(" @ ").Append(inv.ScriptName).Append(':').Append(inv.ScriptLineNumber);
            else if (inv.ScriptLineNumber > 0)
                sb.Append(" @ Zeile ").Append(inv.ScriptLineNumber);
        }

        if (err.Exception?.InnerException is { } inner)
            sb.Append("\n      Ursache: ").Append(inner.Message);
    }
}
