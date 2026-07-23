using System.Collections;
using System.Collections.ObjectModel;
using System.Management.Automation;
using NLog;

namespace DTM.Data.Terminal;

/// <summary>
/// Holt die Backup-Datei-Liste einer MSSQL-DB ueber einen eigenen
/// In-Process-PowerShell-Runspace (Get-DbBackups). Komplementaer zum
/// <see cref="TerminalBus"/>: der TerminalBus streamt Text in den pwsh-Tab,
/// fuer einen strukturierten DataGrid-Dialog brauchen wir aber das
/// PSCustomObject mit Name/Datum/Groesse.
///
/// Gleiches Pattern wie <see cref="OracleRestoreService"/>; Modul-Import
/// teilt sich beim ersten Aufruf die Samba-Copy-Zeit, Folgeaufrufe sind
/// schnell.
/// </summary>
public sealed class BackupBrowserService
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Importiert das FOC-SQL-Modul und ruft <c>Get-DbBackups -Database &lt;db&gt;</c>.
    /// Wirft <see cref="InvalidOperationException"/> bei Modul-Import- oder
    /// Cmdlet-Fehlern.
    /// </summary>
    public Task<IReadOnlyList<MssqlBackup>> FetchAsync(
        string database, string? server = null, CancellationToken ct = default)
    {
        return Task.Run<IReadOnlyList<MssqlBackup>>(() =>
        {
            ct.ThrowIfCancellationRequested();

            using PowerShell ps = PowerShell.Create();

            ps.AddScript(FocSqlRuntime.BuildImportSnippet()).Invoke();

            // Wichtig: Streams.Error nach dem Import kann nicht-fatale Fehler
            // enthalten (z.B. Copy-Item schlaegt fehl wegen Datei-Lock vom
            // parallelen pwsh-Tab — Modul ist trotzdem ueber den PSModulePath
            // erreichbar). Daher hier NICHT direkt werfen, sondern pruefen ob
            // das Cmdlet wirklich verfuegbar ist.
            string importDiag = PowerShellDiagnostics.FormatDiagnostics(ps, "FOC-SQL Modul-Import");
            ps.Streams.ClearStreams();

            if (!PowerShellDiagnostics.CommandExists(ps, "Get-DbBackups"))
            {
                throw new InvalidOperationException(
                    "FOC-SQL Modul ist nicht geladen oder Get-DbBackups fehlt — pruefe "
                    + "ModulePath/SambaSource in den Einstellungen und stelle sicher, dass "
                    + "das aktuelle FOC-SQL-Modul (mit Get-DbBackups) auf der Samba-Quelle "
                    + "liegt.\n\nDiagnose des Import-Versuchs:\n" + importDiag);
            }

            if (ps.HadErrors)
                _logger.Warn("FOC-SQL Modul-Import hatte nicht-fatale Fehler (Cmdlet trotzdem verfuegbar): {0}", importDiag);

            ct.ThrowIfCancellationRequested();
            ps.Commands.Clear();
            ps.Streams.ClearStreams();

            ps.AddCommand("Get-DbBackups")
              .AddParameter("Database", database);
            if (!string.IsNullOrWhiteSpace(server))
                ps.AddParameter("Server", server);

            Collection<PSObject> results = ps.Invoke();
            PowerShellDiagnostics.ThrowIfErrors(ps, $"Get-DbBackups -Database '{database}'");

            List<MssqlBackup> backups = new();
            foreach (PSObject? item in results)
            {
                if (item is null) continue;
                backups.Add(new MssqlBackup(
                    Prop(item, "Name"),
                    PropDate(item, "LastWriteTime"),
                    PropLong(item, "Size"),
                    Prop(item, "Path")));
            }

            _logger.Info("Get-DbBackups({0}): {1} Backup-Datei(en)", database, backups.Count);
            return backups;
        }, ct);
    }

    private static string Prop(PSObject pso, string name) =>
        pso.Properties[name]?.Value?.ToString() ?? string.Empty;

    private static DateTime PropDate(PSObject pso, string name)
    {
        object? v = pso.Properties[name]?.Value;
        if (v is DateTime dt) return dt;
        if (v is string s && DateTime.TryParse(s, out DateTime parsed)) return parsed;
        return DateTime.MinValue;
    }

    private static long PropLong(PSObject pso, string name)
    {
        object? v = pso.Properties[name]?.Value;
        if (v is long l) return l;
        if (v is int i) return i;
        if (v is string s && long.TryParse(s, out long parsed)) return parsed;
        return 0;
    }
}
