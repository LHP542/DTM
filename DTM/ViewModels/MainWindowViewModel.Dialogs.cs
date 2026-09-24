using DTM.ViewModels.TreeNodes;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DTM.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DTM.ViewModels;

/// <summary>
/// Fenster-übergreifende Dialoge, die an keiner Aktions-Gruppe hängen:
/// Verbindungsverwaltung, Sessions-Liste und der Update-Ablauf.
/// </summary>
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task ManageConnections()
    {
        Window? owner = GetMainWindow();
        if (owner is null) return;
        ConnectionManagerWindow dlg = new() { DataContext = ResolveOrNew<ConnectionManagerViewModel>() };
        await dlg.ShowDialog(owner);
        ReloadFromStores();
    }

    [RelayCommand]
    private async Task ShowSessions()
    {
        Window? owner = GetMainWindow();
        if (owner is null) return;

        SessionsViewModel vm = ResolveOrNew<SessionsViewModel>();
        vm.SetSessions(_currentSessions);
        if (SelectedNode is DatabaseNodeViewModel db)
        {
            // MariaDB beendet Verbindungen über KILL, nicht über FOC-SQL
            // oder T-SQL — der Dialog bekommt deshalb den passenden Dienst.
            var maria = db.ServerTyp == DB_SERVER.ServerTyp.MariaDB
                ? _data.GetMariaDbActions(db.ServerIdentity)
                : null;
            vm.Configure(ModuleDatabaseId(db), db.Database.Name, TryGetOdbcActions(db), maria);
        }
        SessionsWindow dlg = new SessionsWindow { DataContext = vm };
        await dlg.ShowDialog(owner);
    }

    // Update-Check gegen GitHub Releases (Klemmbrett-Muster, seit v2.3.0).
    // Wird beim App-Start aufgerufen — der UpdateService cached das Ergebnis,
    // damit der spätere "Auf Updates prüfen"-Klick im AboutWindow keinen
    // zweiten API-Call macht (dort wird via forceRefresh=true umgangen).
    public async Task CheckForUpdateAsync()
    {
        if (_services is null) return;
        try
        {
            var updater = _services.GetRequiredService<DTM.Updater.UpdateService>();
            var result = await updater.CheckForUpdateAsync();
            if (result is not { UpdateAvailable: true }) return;

            // Ist der letzte Austausch gescheitert, wird der Hinweis einmal
            // übersprungen. Sonst entsteht genau die Schleife, in der DTM am
            // 2026-09-24 elfmal hintereinander gelandet ist: alte Version
            // startet, findet dasselbe Paket, lädt, scheitert, startet wieder.
            Version? gescheitert = DTM.Updater.UpdateFailureMarker.Read();
            if (DTM.Updater.UpdateFailureMarker.ShouldSuppress(gescheitert, result.Current, result.Latest))
            {
                _logger.Warn("Update auf {0} wurde beim letzten Versuch nicht eingespielt — "
                             + "automatischer Hinweis übersprungen.", gescheitert);
                StatusBar = $"Das letzte Update auf {gescheitert} konnte nicht eingespielt werden — "
                          + "Details im Log. Erneuter Versuch über ℹ → Auf Updates prüfen.";
                return;
            }

            // Der Marker hat sich erledigt (Version erreicht oder eine neuere
            // liegt bereit) — weg damit, sonst bleibt er ewig liegen.
            if (gescheitert is not null) DTM.Updater.UpdateFailureMarker.Clear();

            await ShowUpdateDialogAsync(updater, result);
        }
        catch (Exception ex) { _logger.Warn(ex, "Update-Prüfung fehlgeschlagen."); }
    }

    private async Task ShowUpdateDialogAsync(DTM.Updater.UpdateService updater, DTM.Updater.UpdateCheckResult update)
    {
        Window? owner = GetMainWindow();
        if (owner is null) return;

        // release-notes.json wird vom UpdateService selbst aus dem Repo-Raw
        // geladen (kein Samba mehr). Der Bereich (current, latest] filtert
        // Einträge — release-notes-Redaktion ist deshalb unabhängig vom
        // Release-Bundle.
        var notes = await updater.LoadReleaseNotesAsync(update.Current, update.Latest);

        var dlg = new UpdatePromptWindow(update.Latest.ToString(), update.Current.ToString(3), notes);
        await dlg.ShowDialog(owner);

        switch (dlg.Result)
        {
            case UpdateDialogResult.ApplyNow:
                _logger.Info("Update wird jetzt angewendet: {0}", update.Latest);
                var applyProgress = new Progress<double>(pct =>
                    StatusBar = $"Update lädt: {pct:P0}");
                bool ok = await updater.DownloadAndApplyAsync(update, applyProgress);
                if (ok)
                {
                    // Austausch-Skript läuft und wartet auf das Prozessende —
                    // App jetzt beenden, sonst hängt es bei „Update lädt: 100 %".
                    StatusBar = "Update wird installiert — Anwendung startet neu …";
                    _logger.Info("Update {0} vorbereitet — App wird beendet, der Installer übernimmt.", update.Latest);
                    DTM.Updater.UpdateService.TerminateForUpdate();
                }
                else
                    StatusBar = "Self-Update nicht möglich — bitte Release-Seite im Browser öffnen.";
                break;
            case UpdateDialogResult.Later:
                _logger.Info("Update auf {0} auf später verschoben (30 min).", update.Latest);
                _ = Task.Delay(TimeSpan.FromMinutes(30))
                        .ContinueWith(_ =>
                            Dispatcher.UIThread.InvokeAsync(() =>
                                ShowUpdateDialogAsync(updater, update)));
                break;
            case UpdateDialogResult.Skip:
                _logger.Info("Update auf {0} für diese Sitzung übersprungen.", update.Latest);
                break;
        }
    }
}
