using DTM.ViewModels.TreeNodes;
using Avalonia.Controls;
using Avalonia.Threading;
using DTM.Views;

namespace DTM.ViewModels;

/// <summary>
/// Backend-Dispatch und gemeinsame Ausfuehrungspfade.
///
/// DTM spricht Datenbanken auf zwei Wegen an: ueber das FOC-SQL-PowerShell-Modul
/// (fire-and-forget im sichtbaren pwsh-Tab) und — seit Phase 10 fuer DMZ-Server
/// ohne WinRM — direkt per ODBC. Die Wahl faellt pro Server ueber
/// <see cref="ServerBackend"/>. Bewusst KEIN gemeinsames Backend-Interface:
/// die beiden Wege haben unvereinbare Signaturen (asynchrones SQL gegen
/// abgesetztes Kommando), deshalb entscheidet jeder Aufrufer selbst per
/// <see cref="TryGetOdbcActions"/>.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Gemeinsamer Pfad für Backup/Clone/Snapshot: Zeit abfragen, dann die
    /// passende FOC-SQL-Modulfunktion über den TerminalBus im pwsh-Tab aufrufen.
    /// Das Modul übernimmt Remoting, Credential-Handling und Scheduling.
    /// </summary>
    private async Task RunDbActionAsync(string focFunction, DatabaseNodeViewModel db, string label)
    {
        TimePickResult pick = await PickTimeAsync();
        if (pick.Cancelled) return;

        DateTime? when = pick.When; // null = sofort
        string whenText = when is { } w ? $"um {w:g}" : "sofort";
        StatusBar = $"{label} für {db.Database.Name} {whenText} …";

        DTM.Data.Terminal.TerminalBus.RunFocSqlAction(
            functionName: focFunction,
            database: ModuleDatabaseId(db),
            when: when,
            title: $"{label} {db.Database.Name}",
            server: ServerParamFor(db),
            onUnavailable: () =>
                Dispatcher.UIThread.Post(() =>
                    StatusBar = $"{label} nicht möglich: pwsh-Tab ist nicht bereit."));

        StatusBar = $"{label} für {db.Database.Name} ausgelöst.";
    }

    // Phase 10.4: liefert den OdbcMssqlActionService, wenn der Server der
    // DB im OdbcDirect-Modus ist. Sonst null → Aufrufer geht den FOC-SQL-Weg.
    // Fuer Oracle immer null (kein OdbcDirect fuer Oracle).
    private DTM.Data.Mssql.OdbcMssqlActionService? TryGetOdbcActions(DatabaseNodeViewModel db)
    {
        var server = _data.Servers.FirstOrDefault(s => s.Identity == db.ServerIdentity);
        if (server?.Backend != ServerBackend.OdbcDirect) return null;
        if (server.Typ != DB_SERVER.ServerTyp.MSSQL) return null;
        return _data.GetMssqlActions(db.ServerIdentity);
    }

    // Phase 10.4: einheitlicher Ausfuehrungspfad fuer OdbcDirect-Actions.
    // Statusbar + Notice-Header/-Footer, Exception-Handling, Live-Output
    // per InjectNotice — der pwsh-Tab sieht die ODBC-Aktion wie eine
    // FOC-SQL-Aktion (nur ohne Live-Stream aus dem Modul).
    private async Task RunOdbcActionAsync(string label, string dbName, Func<Action<string>, Task> action)
    {
        StatusBar = $"{label} für {dbName} …";
        DTM.Data.Terminal.TerminalBus.InjectNotice($"[{label} für {dbName} (OdbcDirect)]");
        try
        {
            Action<string> onInfo = t => DTM.Data.Terminal.TerminalBus.InjectNotice($"  {t}");
            await action(onInfo).ConfigureAwait(false);
            DTM.Data.Terminal.TerminalBus.InjectNotice($"[{label} fertig für {dbName}]");
            await Dispatcher.UIThread.InvokeAsync(() =>
                StatusBar = $"{label} für {dbName} fertig.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "{0} fuer {1} fehlgeschlagen", label, dbName);
            DTM.Data.Terminal.TerminalBus.InjectNotice($"[FEHLER: {ex.Message}]");
            await Dispatcher.UIThread.InvokeAsync(() =>
                StatusBar = $"{label} fehlgeschlagen: {ex.Message}");
        }
    }

    /// <summary>
    /// Aktionen ohne Zeitplanung (Restore/Remove/Archive/Samba). Teils
    /// interaktiv — der Output und etwaige Prompts erscheinen im pwsh-Tab,
    /// Antworten tippt der User in die Befehlszeile.
    /// </summary>
    private void RunSimpleAction(string focFunction, DatabaseNodeViewModel db, string extraArgs, string label)
    {
        StatusBar = $"{label} für {db.Database.Name} …";
        DTM.Data.Terminal.TerminalBus.RunFocSqlSimple(
            functionName: focFunction,
            database: ModuleDatabaseId(db),
            extraArgs: extraArgs,
            title: $"{label} {db.Database.Name}",
            server: ServerParamFor(db),
            onUnavailable: () =>
                Dispatcher.UIThread.Post(() =>
                    StatusBar = $"{label} nicht möglich: pwsh-Tab ist nicht bereit."));
        StatusBar = $"{label} für {db.Database.Name} gestartet — siehe Shell-Tab.";
    }

    /// <summary>
    /// Bezeichner, den die FOC-SQL-Modulfunktionen erwarten:
    /// MSSQL → DB-Name, Oracle → FQDN (das Modul baut daraus 'oracle@&lt;FQDN&gt;'
    /// als SSH-Ziel). Fällt bei fehlendem FQDN auf den Namen zurück.
    /// </summary>
    internal static string ModuleDatabaseId(DatabaseNodeViewModel db) =>
        db.ServerTyp == DB_SERVER.ServerTyp.MSSQL
            ? db.Database.Name
            : (string.IsNullOrWhiteSpace(db.Database.FQDN) ? db.Database.Name : db.Database.FQDN!);

    /// <summary>
    /// Liefert den Server-Hostname fuer den FOC-SQL -Server-Parameter:
    /// MSSQL → konkreter Hostname (mehrere MSSQL-Server unterscheidbar).
    /// Oracle → <c>null</c> (Oracle adressiert ueber FQDN im -Database-Param,
    /// das -Server-Argument geht an die DTM-Wrapper, die es bei Oracle ignorieren).
    /// </summary>
    internal static string? ServerParamFor(DatabaseNodeViewModel db) =>
        db.ServerTyp == DB_SERVER.ServerTyp.MSSQL
            ? db.ServerIdentity.Server
            : null;

    private async Task<TimePickResult> PickTimeAsync()
    {
        Window? owner = GetMainWindow();
        if (owner is null) return TimePickResult.Cancel();

        TimePickerWindow dlg = new TimePickerWindow { DataContext = ResolveOrNew<TimePickerViewModel>() };
        return await dlg.ShowDialog<TimePickResult>(owner);
    }
}
