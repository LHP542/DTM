using System.Collections;
using System.Collections.ObjectModel;
using System.Management.Automation;
using NLog;

namespace DTM.Data.Terminal;

/// <summary>
/// Holt die Oracle-Restore-Vorschau (<see cref="OracleRestoreInfo"/>) ueber
/// einen eigenen In-Process-PowerShell-Runspace. Komplementaer zum
/// <see cref="TerminalBus"/>: der TerminalBus sendet Befehle in den
/// sichtbaren pwsh-Tab und gibt nur Text zurueck — fuer einen strukturierten
/// Dialog brauchen wir aber das PSCustomObject. Daher ein eigener,
/// kurzlebiger Runspace pro Aufruf.
/// </summary>
public sealed class OracleRestoreService
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Importiert das FOC-SQL-Modul und ruft <c>Get-OracleRestoreInfo -Database &lt;db&gt;</c>
    /// auf. Wirft <see cref="InvalidOperationException"/>, wenn der Modul-Import
    /// oder der Cmdlet-Aufruf Fehler meldet.
    /// </summary>
    public Task<OracleRestoreInfo> FetchAsync(string database, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using PowerShell ps = PowerShell.Create();

            // Modul laden — gleiche Snippet-Logik wie der pwsh-Tab; das deckt
            // Samba-Copy/Encoding-Fix/Import in einem Rutsch ab. Erster Aufruf
            // ist teuer (~5 s), Folgeaufrufe schneller (Module ist im PSModulePath).
            ps.AddScript(FocSqlRuntime.BuildImportSnippet()).Invoke();

            // Streams.Error nach dem Import kann nicht-fatale Fehler enthalten
            // (Datei-Lock vom parallelen pwsh-Tab) — pruefen ob das Cmdlet
            // verfuegbar ist statt direkt zu werfen.
            string importDiag = PowerShellDiagnostics.FormatDiagnostics(ps, "FOC-SQL Modul-Import");
            ps.Streams.ClearStreams();

            if (!PowerShellDiagnostics.CommandExists(ps, "Get-OracleRestoreInfo"))
            {
                throw new InvalidOperationException(
                    "FOC-SQL Modul ist nicht geladen oder Get-OracleRestoreInfo fehlt — pruefe "
                    + "ModulePath/SambaSource in den Einstellungen.\n\nDiagnose des Import-Versuchs:\n"
                    + importDiag);
            }

            if (ps.HadErrors)
                _logger.Warn("FOC-SQL Modul-Import hatte nicht-fatale Fehler (Cmdlet trotzdem verfuegbar): {0}", importDiag);

            ct.ThrowIfCancellationRequested();
            ps.Commands.Clear();
            ps.Streams.ClearStreams();

            ps.AddCommand("Get-OracleRestoreInfo")
              .AddParameter("Database", database);

            Collection<PSObject> results = ps.Invoke();
            PowerShellDiagnostics.ThrowIfErrors(ps, $"Get-OracleRestoreInfo -Database '{database}'");

            if (results.Count == 0 || results[0] is null)
            {
                _logger.Warn("Get-OracleRestoreInfo lieferte kein Ergebnis fuer '{0}'.", database);
                return new OracleRestoreInfo(Array.Empty<OraclePdb>(), Array.Empty<OracleRestorePoint>());
            }

            PSObject root = results[0];
            List<OraclePdb> pdbs = ExtractPdbs(root.Properties["PdbNames"]?.Value);
            List<OracleRestorePoint> rps = ExtractRestorePoints(root.Properties["RestorePoints"]?.Value);

            _logger.Info("Get-OracleRestoreInfo({0}): {1} PDB(s), {2} Restore Point(s)",
                database, pdbs.Count, rps.Count);

            return new OracleRestoreInfo(pdbs, rps);
        }, ct);
    }

    private static List<OraclePdb> ExtractPdbs(object? raw)
    {
        List<OraclePdb> result = new();
        if (raw is IEnumerable list)
        {
            foreach (object? item in list)
            {
                if (Unwrap(item) is not PSObject pso) continue;
                result.Add(new OraclePdb(
                    Prop(pso, "Name"),
                    Prop(pso, "ConId")));
            }
        }
        return result;
    }

    private static List<OracleRestorePoint> ExtractRestorePoints(object? raw)
    {
        List<OracleRestorePoint> result = new();
        if (raw is IEnumerable list)
        {
            foreach (object? item in list)
            {
                if (Unwrap(item) is not PSObject pso) continue;
                result.Add(new OracleRestorePoint(
                    Prop(pso, "Name"),
                    Prop(pso, "Time"),
                    Prop(pso, "Guaranteed"),
                    Prop(pso, "ConId"),
                    Prop(pso, "SCN")));
            }
        }
        return result;
    }

    private static PSObject? Unwrap(object? item) => item switch
    {
        PSObject pso => pso,
        null => null,
        _ => PSObject.AsPSObject(item),
    };

    private static string Prop(PSObject pso, string name) =>
        pso.Properties[name]?.Value?.ToString() ?? string.Empty;
}
