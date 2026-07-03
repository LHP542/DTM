using CommunityToolkit.Mvvm.ComponentModel;
using DTM.Data.Mssql;
using DTM.Data.Terminal;

namespace DTM.ViewModels;

/// <summary>
/// ViewModel fuer den DB-Konfigurations-Dialog (Phase 5.1/5.3, MSSQL-only).
/// Drei selten gebrauchte Settings auf einer Stelle:
/// - Query-Store ON/OFF
/// - Page-Verify CHECKSUM / TORN_PAGE_DETECTION / NONE
/// - Compatibility-Level auf MASTER-Default zuruecksetzen
///
/// Bestaetigungs-Dialoge passieren im Code-Behind des Windows
/// (analog SessionsWindow/BackupBrowserWindow); dieses VM stellt nur die
/// „Perform*"-Methoden bereit, die den eigentlichen Aufruf abschicken.
///
/// Phase 10.4: wenn das VM einen <see cref="OdbcMssqlActionService"/>
/// bekommt (via <see cref="Configure"/>), laufen die drei Actions direkt
/// ueber ODBC statt ueber FOC-SQL. Fuer DMZ-Server.
/// </summary>
public sealed partial class DbConfigurationViewModel : ViewModelBase
{
    [ObservableProperty] private string _databaseName = string.Empty;

    /// <summary>MSSQL-Host fuer -Server-Parameter (Multi-Server-Support Phase 6).</summary>
    public string? ServerHost { get; set; }

    /// <summary>Wenn gesetzt: OdbcDirect-Pfad; sonst FOC-SQL-Pfad.</summary>
    public OdbcMssqlActionService? OdbcActions { get; set; }

    // --- Query-Store ---
    [ObservableProperty] private bool _queryStoreOn = true;

    // --- Page-Verify ---
    public IReadOnlyList<string> PageVerifyOptions { get; } =
        new[] { "CHECKSUM", "TORN_PAGE_DETECTION", "NONE" };
    [ObservableProperty] private string _pageVerifySelected = "CHECKSUM";

    // --- Compatibility ---
    [ObservableProperty] private int _compatibilityLevel;

    /// <summary>
    /// Vor dem Anzeigen vom MainWindowViewModel aufzurufen — uebergibt
    /// Kontext (DB-Name, Server-Host) und aktuelle Werte aus den Stats
    /// als Vorauswahl/Anzeige.
    /// </summary>
    public void Configure(
        string database,
        string? serverHost,
        string? currentPageVerify,
        int currentCompatibility,
        OdbcMssqlActionService? odbcActions = null)
    {
        DatabaseName = database;
        ServerHost = serverHost;
        OdbcActions = odbcActions;
        if (!string.IsNullOrWhiteSpace(currentPageVerify)
            && PageVerifyOptions.Contains(currentPageVerify))
        {
            PageVerifySelected = currentPageVerify;
        }
        CompatibilityLevel = currentCompatibility;
        // Query-Store-Status ist nicht in den Stats; Default true (User toggelt).
        QueryStoreOn = true;
    }

    public void PerformQueryStore()
    {
        if (string.IsNullOrWhiteSpace(DatabaseName)) return;
        string label = QueryStoreOn ? "Query-Store ON" : "Query-Store OFF";

        if (OdbcActions is { } svc)
        {
            _ = RunOdbcAsync(label, onInfo => svc.SetQueryStoreAsync(DatabaseName, QueryStoreOn, onInfo));
            return;
        }

        string extra = QueryStoreOn ? string.Empty : "-Off";
        TerminalBus.RunFocSqlSimple(
            functionName: "Set-DbQueryStore",
            database: DatabaseName,
            extraArgs: extra,
            title: label,
            server: ServerHost);
    }

    public void PerformPageVerify()
    {
        if (string.IsNullOrWhiteSpace(DatabaseName)) return;
        string label = $"Page-Verify → {PageVerifySelected}";

        if (OdbcActions is { } svc)
        {
            _ = RunOdbcAsync(label, onInfo => svc.SetPageVerifyAsync(DatabaseName, PageVerifySelected, onInfo));
            return;
        }

        TerminalBus.RunFocSqlSimple(
            functionName: "Set-DbPageVerify",
            database: DatabaseName,
            extraArgs: $"-PageVerify {PageVerifySelected}",
            title: label,
            server: ServerHost);
    }

    public void PerformCompatibilityReset()
    {
        if (string.IsNullOrWhiteSpace(DatabaseName)) return;
        const string label = "Compatibility-Reset";

        if (OdbcActions is { } svc)
        {
            _ = RunOdbcAsync(label, onInfo => svc.ResetCompatibilityAsync(DatabaseName, onInfo));
            return;
        }

        TerminalBus.RunFocSqlSimple(
            functionName: "Reset-DbCompatibility",
            database: DatabaseName,
            extraArgs: string.Empty,
            title: label,
            server: ServerHost);
    }

    private async Task RunOdbcAsync(string label, Func<Action<string>, Task> op)
    {
        TerminalBus.InjectNotice($"[{label} für {DatabaseName} (OdbcDirect)]");
        try
        {
            Action<string> onInfo = t => TerminalBus.InjectNotice($"  {t}");
            await op(onInfo).ConfigureAwait(false);
            TerminalBus.InjectNotice($"[{label} fertig]");
        }
        catch (Exception ex)
        {
            TerminalBus.InjectNotice($"[FEHLER: {ex.Message}]");
        }
    }
}
