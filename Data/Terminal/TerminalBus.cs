using NLog;

namespace DTM.Data.Terminal;

/// <summary>
/// Singleton-Mediator zwischen den DTM-Aktionen (Backup/Clone/Snapshot etc.)
/// und der pwsh-Console im UI. Die ViewModel ruft <see cref="RunFocSqlAction"/>
/// bzw. <see cref="RunFocSqlSimple"/>; die aktuell registrierte
/// <see cref="ITerminalSession"/> (siehe <see cref="RegisterPowerShellSession"/>)
/// führt den FOC-SQL-Modulaufruf aus, sodass der Output live im pwsh-Tab erscheint.
/// Wenn kein pwsh-Tab läuft, wird der optionale onUnavailable-Fallback aufgerufen.
/// </summary>
public enum TerminalLineKind { Output, Error }

public static class TerminalBus
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private static readonly object _lock = new();
    private static ITerminalSession? _powerShellSession;

    // Phase 9.5: gecachete Server-Liste, damit die $global:DtmCredMap injiziert
    // werden kann sobald die pwsh-Session registriert wird — die Server-Liste
    // ist beim App-Start bekannt, die Session laeuft aber erst wenn das
    // ConsoleControl attached. Bei ConnectionManager-Save wird SetCredMap
    // erneut aufgerufen und (falls Session aktiv) sofort re-injiziert.
    private static IReadOnlyList<DB_SERVER>? _servers;

    /// <summary>
    /// Wird fuer jede Output-/Error-Zeile gefeuert, die durch die aktuell
    /// registrierte Session laeuft. Erlaubt der UI (MainWindowViewModel),
    /// nach bestimmten Patterns zu lauschen (z. B. VERSION_MISMATCH aus
    /// FOC-SQL) und im StatusBar zu spiegeln, ohne dass der User das
    /// pwsh-Log selbst durchscrollen muss.
    /// </summary>
    public static event EventHandler<TerminalLineEventArgs>? LineEmitted;

    /// <summary>
    /// Wird vom <c>ConsoleControl</c> aufgerufen, sobald eine PowerShell-Session
    /// bereit ist. Mehrfach-Aufrufe sind okay: die letzte Registrierung gewinnt
    /// (etwa wenn das Control neu attached oder die Session reconnected).
    /// </summary>
    public static void RegisterPowerShellSession(ITerminalSession session)
    {
        lock (_lock)
        {
            if (_powerShellSession is { } old)
            {
                old.OutputReceived -= ForwardOutput;
                old.ErrorReceived  -= ForwardError;
            }
            _powerShellSession = session;
            session.OutputReceived += ForwardOutput;
            session.ErrorReceived  += ForwardError;
        }
        _logger.Debug("TerminalBus: PowerShell-Session registriert.");
        InjectCredMapIfAvailable();
    }

    /// <summary>
    /// Phase 9.5: setzt / aktualisiert die Server-Liste, aus der
    /// <c>$global:DtmCredMap</c> gebaut wird. Beim App-Start einmal, beim
    /// Connection-Manager-Save nach dem Reload. Injiziert sofort, wenn eine
    /// pwsh-Session bereits laeuft — sonst passiert die Injektion sobald die
    /// Session registriert wird.
    /// </summary>
    public static void SetCredMap(IReadOnlyList<DB_SERVER> servers)
    {
        lock (_lock) _servers = servers;
        InjectCredMapIfAvailable();
    }

    private static void InjectCredMapIfAvailable()
    {
        ITerminalSession? sess;
        IReadOnlyList<DB_SERVER>? servers;
        lock (_lock)
        {
            sess = _powerShellSession;
            servers = _servers;
        }
        if (sess is not PowerShellTerminalSession psSess || servers is null) return;

        try
        {
            var map = DtmCredMapBuilder.Build(servers);
            psSess.SetGlobalVariable("DtmCredMap", map);
            _logger.Info("TerminalBus: $global:DtmCredMap injiziert ({0} Server mit Remote-Credentials).", map.Count);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "TerminalBus: DtmCredMap-Injektion fehlgeschlagen.");
        }
    }

    /// <summary>Hebt die Registrierung auf (wenn das Control disposed wird).</summary>
    public static void UnregisterPowerShellSession(ITerminalSession session)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_powerShellSession, session))
            {
                session.OutputReceived -= ForwardOutput;
                session.ErrorReceived  -= ForwardError;
                _powerShellSession = null;
            }
        }
        _logger.Debug("TerminalBus: PowerShell-Session deregistriert.");
    }

    private static void ForwardOutput(object? _, string line)
        => LineEmitted?.Invoke(null, new TerminalLineEventArgs(TerminalLineKind.Output, line));

    private static void ForwardError(object? _, string line)
        => LineEmitted?.Invoke(null, new TerminalLineEventArgs(TerminalLineKind.Error, line));

    /// <summary>Ob aktuell eine pwsh-Session zum Routen verfügbar ist.</summary>
    public static bool HasPowerShellSession
    {
        get { lock (_lock) return _powerShellSession is { IsRunning: true }; }
    }

    /// <summary>
    /// Ruft eine FOC-SQL-Modulfunktion (Backup-Database, Set-Snapshot,
    /// Sync-Database-ToTest) im pwsh-Tab auf. Das Modul ist im Initial-Setup
    /// bereits importiert; wir senden nur noch den Funktionsaufruf. Das Modul
    /// macht sein eigenes Remoting zu den DB-Servern. Output erscheint live im Tab.
    /// </summary>
    /// <param name="functionName">z.B. "Backup-Database".</param>
    /// <param name="database">Datenbankname (MSSQL) bzw. FQDN (Oracle).</param>
    /// <param name="when">Geplanter Zeitpunkt oder null = sofort.</param>
    /// <param name="title">Header-Zeile im Tab.</param>
    /// <param name="server">
    /// Optional: Hostname des MSSQL-Servers (-Server &lt;host&gt; ans Cmdlet
    /// anhaengen). Bei null bleibt der Modul-Default ($global:Server) — fuer
    /// Oracle-Aufrufe irrelevant (Oracle bekommt das Ziel ueber den FQDN
    /// im -Database-Parameter mit). Wird in Phase 6 (Multi-Server-Support)
    /// benoetigt, damit DTM mehrere MSSQL-Hosts unterscheiden kann.
    /// </param>
    /// <param name="onUnavailable">Fallback wenn kein pwsh-Tab aktiv ist.</param>
    public static void RunFocSqlAction(
        string functionName, string database, DateTime? when,
        string title, string? server = null, Action? onUnavailable = null)
    {
        ITerminalSession? sess;
        lock (_lock) sess = _powerShellSession;

        if (sess is null || !sess.IsRunning)
        {
            _logger.Warn("TerminalBus: keine aktive pwsh-Session für {0}", functionName);
            onUnavailable?.Invoke();
            return;
        }

        string timing = when.HasValue ? $"geplant {when.Value:g}" : "sofort";
        _logger.Info("TerminalBus: {0} für '{1}'@'{2}' ({3})",
            functionName, database, server ?? "<default>", timing);

        if (sess is ITerminalBusInjector injector)
        {
            injector.InjectNotice($"[Hintergrund-Job: {title} — {timing}]");
            injector.InjectNotice("[Script läuft, Output folgt …]");
        }

        string call = FocSqlRuntime.BuildCall(functionName, database, when);
        if (!string.IsNullOrWhiteSpace(server))
        {
            string srvEsc = server.Replace("'", "''");
            call += $" -Server '{srvEsc}'";
        }
        _ = sess.SendCommandAsync(call);
    }

    /// <summary>
    /// Ruft eine FOC-SQL-Funktion ohne Zeitparameter auf (Restore-Snapshot,
    /// Remove-Snapshot, Set-Archive-Log, Copy-Database-ToSamba). Diese sind
    /// teils interaktiv (Snapshot-Auswahl, ja/nein) — die Prompts erscheinen
    /// im Tab und werden über die Input-Box beantwortet (DtmPSHost).
    /// </summary>
    /// <param name="functionName">z.B. "Restore-Snapshot".</param>
    /// <param name="database">Datenbankname.</param>
    /// <param name="extraArgs">Zusätzliche Argumente, z.B. "-Off" für Archive-Log.</param>
    /// <param name="title">Header im Tab.</param>
    /// <param name="server">
    /// Optional: MSSQL-Host (-Server &lt;host&gt; ans Cmdlet). Siehe
    /// <see cref="RunFocSqlAction"/> fuer Details.
    /// </param>
    /// <param name="onUnavailable">Fallback wenn kein pwsh-Tab aktiv.</param>
    public static void RunFocSqlSimple(
        string functionName, string database, string extraArgs,
        string title, string? server = null, Action? onUnavailable = null)
    {
        ITerminalSession? sess;
        lock (_lock) sess = _powerShellSession;

        if (sess is null || !sess.IsRunning)
        {
            _logger.Warn("TerminalBus: keine aktive pwsh-Session für {0}", functionName);
            onUnavailable?.Invoke();
            return;
        }

        _logger.Info("TerminalBus: {0} für '{1}'@'{2}'",
            functionName, database, server ?? "<default>");

        if (sess is ITerminalBusInjector injector)
        {
            injector.InjectNotice($"[Aktion: {title}]");
            injector.InjectNotice("[Falls Eingaben nötig sind, bitte in die Befehlszeile tippen]");
        }

        string dbEsc = database.Replace("'", "''");
        string call = $"{functionName} -Database '{dbEsc}'";
        if (!string.IsNullOrWhiteSpace(extraArgs))
            call += " " + extraArgs;
        if (!string.IsNullOrWhiteSpace(server))
        {
            string srvEsc = server.Replace("'", "''");
            call += $" -Server '{srvEsc}'";
        }

        _ = sess.SendCommandAsync(call);
    }

    /// <summary>
    /// Ruft eine FOC-SQL-Funktion auf, die <c>-Server</c> statt <c>-Database</c>
    /// nimmt (aktuell nur <c>Get-ClusterHealthStatus</c>). Sonst analog zu
    /// <see cref="RunFocSqlSimple"/>.
    /// </summary>
    public static void RunFocSqlServerAction(
        string functionName, string server, string title, Action? onUnavailable = null)
    {
        ITerminalSession? sess;
        lock (_lock) sess = _powerShellSession;

        if (sess is null || !sess.IsRunning)
        {
            _logger.Warn("TerminalBus: keine aktive pwsh-Session fuer {0}", functionName);
            onUnavailable?.Invoke();
            return;
        }

        _logger.Info("TerminalBus: {0} fuer Server '{1}'", functionName, server);

        if (sess is ITerminalBusInjector injector)
            injector.InjectNotice($"[Aktion: {title}]");

        string serverEsc = server.Replace("'", "''");
        string call = $"{functionName} -Server '{serverEsc}'";
        _ = sess.SendCommandAsync(call);
    }

    /// <summary>
    /// Injiziert eine synthetische Notice-Zeile in den pwsh-Tab, ohne
    /// dass dafuer ein PS-Command ausgefuehrt werden muss. Genutzt von
    /// OdbcDirect-Actions (Phase 10.4), damit Backup-/Restore-/Wartungs-
    /// Output im selben pwsh-Tab landet wie der FOC-SQL-Live-Stream — der
    /// User sieht alles an einem Ort. Wenn keine Session aktiv ist,
    /// nur ins Log.
    /// </summary>
    public static void InjectNotice(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        ITerminalSession? sess;
        lock (_lock) sess = _powerShellSession;
        if (sess is ITerminalBusInjector injector) injector.InjectNotice(text);
        else _logger.Info(text);
    }

    /// <summary>
    /// Sendet ein beliebiges PowerShell-Skript an die laufende Session.
    /// Fire-and-forget — kein Fehler wenn kein Tab aktiv ist.
    /// </summary>
    public static void SendScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script)) return;
        ITerminalSession? sess;
        lock (_lock) sess = _powerShellSession;
        if (sess is null || !sess.IsRunning) return;
        _logger.Debug("TerminalBus: Skript gesendet.");
        _ = sess.SendCommandAsync(script);
    }
}

/// <summary>
/// Optionale Schnittstelle für Sessions, die "synthetische Notices" anzeigen
/// können (z.B. Header für Hintergrund-Jobs). Wenn eine Session das nicht
/// implementiert, fehlt nur die Headerzeile - der Job läuft trotzdem.
/// </summary>
public interface ITerminalBusInjector
{
    void InjectNotice(string text);
}

public sealed class TerminalLineEventArgs : EventArgs
{
    public TerminalLineKind Kind { get; }
    public string Line { get; }
    public TerminalLineEventArgs(TerminalLineKind kind, string line)
    {
        Kind = kind;
        Line = line;
    }
}
