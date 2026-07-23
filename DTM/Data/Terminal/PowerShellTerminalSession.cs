using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using NLog;

namespace DTM.Data.Terminal;

/// <summary>
/// Hostet PowerShell 7 in-process über Microsoft.PowerShell.SDK.
/// Statt Process.Start + pwsh.exe läuft alles im selben Prozess; Variablen
/// und Module bleiben zwischen Befehlen erhalten.
///
/// Befehle (Modulaufrufe wie Backup-Database UND vom User getippte Befehle)
/// laufen lokal im Runspace. Das Remoting zu den DB-Servern übernimmt das
/// FOC-SQL-Modul selbst — DTM hält keine eigene PSSession.
/// </summary>
public sealed class PowerShellTerminalSession : ITerminalSession, ITerminalBusInjector
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    /// <summary>Optionales Setup-Skript, das einmal beim Start läuft.</summary>
    public string? InitialScript { get; }

    private Runspace? _runspace;
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private readonly DtmPSHostUI _hostUi = new();
    // True solange ein Befehl im Runspace läuft. Eine User-Eingabe während
    // dieser Zeit ist eine Antwort auf einen Read-Host-Prompt, KEIN neuer Befehl.
    private volatile bool _commandRunning;

    /// <summary>Ob die Session gerade auf eine interaktive Eingabe wartet.</summary>
    public bool IsAwaitingInput => _commandRunning;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<string>? ErrorReceived;
    public event EventHandler<string>? Notice;
    public event EventHandler? SessionEnded;

    public bool IsRunning => _runspace?.RunspaceStateInfo.State == RunspaceState.Opened;

    public PowerShellTerminalSession(string? initialScript = null)
    {
        InitialScript = initialScript;
    }

    // Defense in depth: falls die Telemetry-Opt-Out-Env-Variable aus irgendeinem
    // Grund nicht greift, schlucken wir AppInsights-Envelopes hier raus, statt
    // sie ins UI durchzulassen.
    private static bool IsTelemetryNoise(string? line)
    {
        if (string.IsNullOrEmpty(line)) return false;
        ReadOnlySpan<char> s = line.AsSpan().TrimStart();
        return s.StartsWith("Application Insights Telemetry:", StringComparison.Ordinal)
            || (s.StartsWith("{\"name\":", StringComparison.Ordinal) && s.Contains("\"iKey\":", StringComparison.Ordinal));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Host-UI-Callbacks: Prompt-Texte und Host-Output gehen an unsere Events.
            _hostUi.OnOutput = text => { if (!IsTelemetryNoise(text)) OutputReceived?.Invoke(this, text); };
            _hostUi.OnError = text => { if (!IsTelemetryNoise(text)) ErrorReceived?.Invoke(this, text); };
            _hostUi.OnPrompt = text => OutputReceived?.Invoke(this, text);

            // Runspace mit Custom-Host: ermöglicht Read-Host/Confirm/PromptForChoice
            // (Restore-Snapshot, Remove-Snapshot, Confirm-Action im Modul).
            var host = new DtmPSHost(_hostUi);
            _runspace = RunspaceFactory.CreateRunspace(host);
            _runspace.Open();
            _logger.Info("PowerShell-Runspace geöffnet.");
            Notice?.Invoke(this, "[PowerShell-Runspace geöffnet]");

            if (!string.IsNullOrWhiteSpace(InitialScript))
            {
                Notice?.Invoke(this, "[Initial-Setup wird ausgeführt …]");
                await ExecuteRawAsync(InitialScript!, cancellationToken).ConfigureAwait(false);
                _logger.Info("Initial-Script ausgeführt.");
                Notice?.Invoke(this, "[Initial-Setup abgeschlossen]");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PowerShell-Start fehlgeschlagen.");
            ErrorReceived?.Invoke(this, $"[PowerShell-Start fehlgeschlagen: {ex.Message}]");
            SessionEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        // Wenn gerade ein Befehl läuft, ist diese Eingabe eine Antwort auf einen
        // Read-Host/Confirm-Prompt des laufenden Befehls — NICHT ein neuer Befehl.
        // Auch leere Eingaben (Enter) sind hier gültige Antworten.
        if (_commandRunning)
        {
            _hostUi.ProvideInput(command ?? string.Empty);
            return;
        }

        if (string.IsNullOrWhiteSpace(command)) return;

        // Alle Befehle laufen lokal im Runspace. Output wird durch Out-String
        // formatiert. Remoting zu DB-Servern macht das FOC-SQL-Modul selbst.
        string script = WrapCommand(command);

        await ExecuteRawAsync(script, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Verpackt einen Befehl. Output wird IMMER durch <c>Out-String -Stream</c>
    /// geschickt, damit der Default-Formatter Tabellen erzeugt (statt
    /// 'System.Diagnostics.Process'-ToString). Im SDK-Hosting fehlt sonst der
    /// Out-Default-Pfad, den pwsh.exe automatisch anhängt.
    ///
    /// Der Befehl wird Base64-kodiert, um Quoting-Probleme zwischen C# und
    /// PowerShell zu vermeiden. Ausführung immer lokal im Runspace.
    /// </summary>
    private static string WrapCommand(string userCommand)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(userCommand));

        return $@"
$__cmd = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{encoded}'))
$__sb  = [scriptblock]::Create($__cmd)
& {{ & $__sb }} | Out-String -Stream -Width 200
Remove-Variable __cmd, __sb -ErrorAction SilentlyContinue
";
    }

    private async Task ExecuteRawAsync(string script, CancellationToken cancellationToken)
    {
        if (_runspace is null || _runspace.RunspaceStateInfo.State != RunspaceState.Opened)
        {
            _logger.Warn("ExecuteRawAsync: Runspace nicht offen.");
            ErrorReceived?.Invoke(this, "[PowerShell-Runspace nicht offen]");
            return;
        }

        await _executionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        _commandRunning = true;
        _logger.Debug("PowerShell-Ausführung startet.");
        try
        {
            using var ps = PowerShell.Create();
            ps.Runspace = _runspace;
            ps.AddScript(script);

            var output = new PSDataCollection<PSObject>();
            output.DataAdded += (_, e) =>
            {
                var obj = output[e.Index];
                if (obj is null) return;
                string text = obj.ToString();
                if (IsTelemetryNoise(text)) return;
                _logger.Debug(text);
                OutputReceived?.Invoke(this, text + Environment.NewLine);
            };

            ps.Streams.Error.DataAdded += (_, e) =>
            {
                var err = ps.Streams.Error[e.Index];
                string text = err.ToString();
                if (IsTelemetryNoise(text)) return;
                _logger.Warn("ERROR: {0}", text);
                ErrorReceived?.Invoke(this, text + Environment.NewLine);
            };
            ps.Streams.Warning.DataAdded += (_, e) =>
            {
                var w = ps.Streams.Warning[e.Index];
                if (IsTelemetryNoise(w.Message)) return;
                Notice?.Invoke(this, $"WARNING: {w.Message}");
            };
            ps.Streams.Information.DataAdded += (_, e) =>
            {
                var info = ps.Streams.Information[e.Index];

                // Write-Host wird in PowerShell 5+ als Information-Record mit Tag
                // "PSHOST" geführt UND zusätzlich über die Host-UI (DtmPSHostUI.Write)
                // ausgegeben. Da der Custom-Host das bereits anzeigt, würde ein
                // erneutes Ausgeben hier zu DOPPELTER Ausgabe führen → überspringen.
                if (info.Tags?.Contains("PSHOST") == true) return;

                string text = info.MessageData?.ToString() ?? string.Empty;
                if (IsTelemetryNoise(text)) return;
                Notice?.Invoke(this, text);
            };
            ps.Streams.Verbose.DataAdded += (_, e) =>
            {
                var v = ps.Streams.Verbose[e.Index];
                if (IsTelemetryNoise(v.Message)) return;
                Notice?.Invoke(this, $"VERBOSE: {v.Message}");
            };

            // Async-Ausführung, damit der UI-Thread frei bleibt.
            await Task.Factory.FromAsync(
                (cb, st) => ps.BeginInvoke<PSObject, PSObject>(input: null, output, settings: null, cb, st),
                ps.EndInvoke,
                state: null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PowerShell-Ausführung fehlgeschlagen.");
            ErrorReceived?.Invoke(this, $"[PowerShell-Fehler: {ex.Message}]");
        }
        finally
        {
            _commandRunning = false;
            _executionLock.Release();
        }
    }

    public Task StopAsync()
    {
        try
        {
            // Wartende Read-Host/Confirm-Operationen abbrechen, sonst hängt
            // der Reader-Thread im Monitor.Wait.
            _hostUi.Cancel();
            if (_runspace is not null)
            {
                try { _runspace.Close(); } catch { /* swallow */ }
                _runspace.Dispose();
            }
        }
        catch { /* swallow */ }
        finally
        {
            _runspace = null;
            _logger.Info("PowerShell-Runspace gestoppt.");
            SessionEnded?.Invoke(this, EventArgs.Empty);
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); }
        catch { /* swallow */ }
        _executionLock.Dispose();
    }

    /// <summary>
    /// Erlaubt dem TerminalBus, Hintergrund-Job-Header in den Output-Stream
    /// einzuschleusen, ohne dass dafür ein PS-Befehl ausgeführt werden muss.
    /// </summary>
    public void InjectNotice(string text)
    {
        _logger.Info(text);
        Notice?.Invoke(this, text);
    }

    /// <summary>
    /// Setzt eine globale Runspace-Variable direkt via SessionStateProxy —
    /// KEIN Command-Interpreter, keine Command-Line, kein Log-Leak. Genutzt
    /// von Phase 9.5, um <c>$global:DtmCredMap</c> (PSCredential-Hashtable)
    /// in den Runspace zu geben, ohne dass Passwoerter je durch den
    /// Terminal-Ausgabe-Pfad laufen.
    /// </summary>
    public void SetGlobalVariable(string name, object? value)
    {
        if (_runspace is null || _runspace.RunspaceStateInfo.State != RunspaceState.Opened)
        {
            _logger.Debug("SetGlobalVariable('{0}') ignoriert: Runspace nicht offen.", name);
            return;
        }
        _runspace.SessionStateProxy.SetVariable(name, value);
        _logger.Debug("Runspace-Variable '{0}' gesetzt.", name);
    }
}
