using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DTM.Data.Mssql;
using DTM.Data.Terminal;

namespace DTM.ViewModels;

public sealed partial class SessionsViewModel : ViewModelBase
{
    public ObservableCollection<Session> Sessions { get; } = new();

    /// <summary>Bezeichner fuer FOC-SQL (MSSQL: DB-Name, Oracle: FQDN).</summary>
    [ObservableProperty] private string _focDatabaseId = string.Empty;

    /// <summary>Anzeige-Name fuer den Confirm-Dialog/Footer.</summary>
    [ObservableProperty] private string _databaseDisplayName = "—";

    /// <summary>Zeigt an, ob die Kill-Session-Aktion verfuegbar ist.</summary>
    [ObservableProperty] private bool _canCloseSessions;

    /// <summary>Wenn gesetzt: OdbcDirect-Pfad; sonst FOC-SQL-Pfad.</summary>
    public OdbcMssqlActionService? OdbcActions { get; set; }

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
    /// optionaler OdbcActionService fuer den OdbcDirect-Pfad.
    /// </summary>
    public void Configure(string focDatabaseId, string displayName,
                          OdbcMssqlActionService? odbcActions = null)
    {
        FocDatabaseId = focDatabaseId;
        DatabaseDisplayName = displayName;
        OdbcActions = odbcActions;
        CanCloseSessions = !string.IsNullOrWhiteSpace(focDatabaseId);
    }

    /// <summary>
    /// Schickt den eigentlichen Close-DbSessions-Aufruf an den pwsh-Tab.
    /// Bestaetigung passiert im Code-Behind des SessionsWindow (ConfirmWindow);
    /// diese Methode setzt die Aktion ohne weitere Rueckfrage ab.
    /// </summary>
    public void PerformCloseAllSessions()
    {
        if (!CanCloseSessions) return;

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

    private async Task RunOdbcAsync(OdbcMssqlActionService svc)
    {
        string label = $"Alle Sessions zu {DatabaseDisplayName} beenden (OdbcDirect)";
        TerminalBus.InjectNotice($"[{label}]");
        try
        {
            Action<string> onInfo = t => TerminalBus.InjectNotice($"  {t}");
            await svc.KillUserSessionsAsync(FocDatabaseId, onInfo).ConfigureAwait(false);
            TerminalBus.InjectNotice("[Sessions beendet]");
        }
        catch (Exception ex)
        {
            TerminalBus.InjectNotice($"[FEHLER: {ex.Message}]");
        }
    }
}
