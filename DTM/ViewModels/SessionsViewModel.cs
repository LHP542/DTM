using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DTM.Data.Mssql;
using DTM.Data.Terminal;

namespace DTM.ViewModels;

public sealed partial class SessionsViewModel : ViewModelBase
{
    public ObservableCollection<Session> Sessions { get; } = new();

    /// <summary>Bezeichner für FOC-SQL (MSSQL: DB-Name, Oracle: FQDN).</summary>
    [ObservableProperty] private string _focDatabaseId = string.Empty;

    /// <summary>Anzeige-Name für den Confirm-Dialog/Footer.</summary>
    [ObservableProperty] private string _databaseDisplayName = "—";

    /// <summary>Zeigt an, ob die Kill-Session-Aktion verfügbar ist.</summary>
    [ObservableProperty] private bool _canCloseSessions;

    /// <summary>Wenn gesetzt: OdbcDirect-Pfad; sonst FOC-SQL-Pfad.</summary>
    public OdbcMssqlActionService? OdbcActions { get; set; }

    /// <summary>Wenn gesetzt: MariaDB-Pfad (hat Vorrang vor beiden anderen).</summary>
    public DTM.Data.MariaDb.MariaDbActionService? MariaDbActions { get; set; }

    public void SetSessions(IEnumerable<Session>? sessions)
    {
        Sessions.Clear();
        if (sessions is null) return;
        foreach (Session s in sessions)
        {
            Sessions.Add(s);
        }
    }

    /// <summary>
    /// Vor dem Anzeigen vom MainWindowViewModel aufzurufen — setzt DB-Kontext,
    /// damit der „Alle Sessions beenden"-Button die richtige DB ansteuert.
    /// Wenn nicht gesetzt, bleibt der Button deaktiviert. Phase 10.4:
    /// optionaler OdbcActionService für den OdbcDirect-Pfad.
    /// </summary>
    public void Configure(string focDatabaseId, string displayName,
                          OdbcMssqlActionService? odbcActions = null,
                          DTM.Data.MariaDb.MariaDbActionService? mariaDbActions = null)
    {
        FocDatabaseId = focDatabaseId;
        DatabaseDisplayName = displayName;
        OdbcActions = odbcActions;
        MariaDbActions = mariaDbActions;
        CanCloseSessions = !string.IsNullOrWhiteSpace(focDatabaseId);
    }

    /// <summary>
    /// Schickt den eigentlichen Close-DbSessions-Aufruf an den pwsh-Tab.
    /// Bestätigung passiert im Code-Behind des SessionsWindow (ConfirmWindow);
    /// diese Methode setzt die Aktion ohne weitere Rückfrage ab.
    /// </summary>
    public void PerformCloseAllSessions()
    {
        if (!CanCloseSessions) return;

        if (MariaDbActions is { } maria)
        {
            _ = RunAsync($"Alle Verbindungen zu {DatabaseDisplayName} beenden (MariaDB)",
                onInfo => maria.KillSessionsAsync(FocDatabaseId, onInfo));
            return;
        }

        if (OdbcActions is { } svc)
        {
            _ = RunOdbcAsync(svc);
            return;
        }

        TerminalBus.RunFocSqlSimple(
            functionName: "Close-DbSessions",
            database: FocDatabaseId,
            extraArgs: string.Empty,
            title: $"Alle Sessions zu {DatabaseDisplayName} beenden");
    }

    private Task RunOdbcAsync(OdbcMssqlActionService svc) =>
        RunAsync($"Alle Sessions zu {DatabaseDisplayName} beenden (OdbcDirect)",
            onInfo => svc.KillUserSessionsAsync(FocDatabaseId, onInfo));

    /// <summary>
    /// Führt eine Aktion aus und spiegelt Start, Fortschritt und Ende als
    /// Notices in den pwsh-Tab — damit sieht der Nutzer bei allen Backends
    /// dasselbe, egal ob die Arbeit über FOC-SQL, ODBC oder MariaDB läuft.
    /// </summary>
    private static async Task RunAsync(string label, Func<Action<string>, Task> action)
    {
        TerminalBus.InjectNotice($"[{label}]");
        try
        {
            Action<string> onInfo = t => TerminalBus.InjectNotice($"  {t}");
            await action(onInfo).ConfigureAwait(false);
            TerminalBus.InjectNotice("[Sessions beendet]");
        }
        catch (Exception ex)
        {
            TerminalBus.InjectNotice($"[FEHLER: {ex.Message}]");
        }
    }
}
