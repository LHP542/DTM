using System.Diagnostics;
using System.Reflection;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DTM.Updater;
using Microsoft.Extensions.DependencyInjection;

namespace DTM.Views;

public partial class AboutWindow : ChromeWindow
{
    private const string GitHubUrl = "https://github.com/Kroste/DTM";
    private const string BuyMeCoffeeUrl = "https://buymeacoffee.com/kroste";

    public AboutWindow()
    {
        InitializeComponent();

        // InformationalVersion enthält den vollen SDK-String (z.B. "1.0.2+abc123…").
        // Alles ab dem '+' (Git-Commit-Hash) wird abgeschnitten.
        var rawVer = Assembly.GetExecutingAssembly()
                             .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                             ?.InformationalVersion ?? "—";
        VersionText.Text = $"Version {rawVer.Split('+')[0]}";

        var logoUri = new Uri("avares://DTM/Assets/lhp_logo.png");
        if (AssetLoader.Exists(logoUri))
            LogoImage.Source = new Bitmap(AssetLoader.Open(logoUri));
        else
            LogoImage.IsVisible = false;
    }

    private void OnOpenGitHub(object? _, RoutedEventArgs e) => OpenUrl(GitHubUrl);
    private void OnOpenBuyMeCoffee(object? _, RoutedEventArgs e) => OpenUrl(BuyMeCoffeeUrl);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Wenn der Default-Browser nicht startbar ist (z.B. headless),
            // bewusst still ignorieren — der Link steht im UI-Tooltip.
        }
    }

    // Manueller "Auf Updates pruefen"-Button (Klemmbrett-Muster): forceRefresh=true
    // umgeht den Cache und macht einen frischen GitHub-API-Call. Der App-Start
    // hat schon einen Check gemacht — dieser hier ist fuer "ich moechte JETZT
    // nochmal nachschauen ob was Neues da ist".
    private async void OnCheckUpdate(object? _, RoutedEventArgs e)
    {
        UpdateCheckButton.IsEnabled = false;
        UpdateStatusText.Text = "Prüfe auf Updates …";

        try
        {
            var updater = App.Services.GetRequiredService<UpdateService>();
            var result = await updater.CheckForUpdateAsync(forceRefresh: true);

            if (result is null)
            {
                UpdateStatusText.Text = "Update-Check fehlgeschlagen (offline/Proxy?).";
                UpdateCheckButton.IsEnabled = true;
                return;
            }

            if (!result.UpdateAvailable)
            {
                UpdateStatusText.Text = $"Aktuell — Version {result.Current} ist die neueste.";
                UpdateCheckButton.IsEnabled = true;
                return;
            }

            // Update gefunden → UpdatePromptWindow oeffnen (mit release-notes.json
            // aus dem Repo-Raw).
            var notes = await updater.LoadReleaseNotesAsync(result.Current, result.Latest);
            var dlg = new UpdatePromptWindow(result.Latest.ToString(), result.Current.ToString(3), notes);
            await dlg.ShowDialog(this);

            if (dlg.Result == UpdateDialogResult.ApplyNow)
            {
                UpdateCheckButton.IsEnabled = false;
                UpdateStatusText.Text = "Update laedt …";
                var progress = new Progress<double>(pct =>
                    UpdateStatusText.Text = $"Update laedt: {pct:P0}");
                bool ok = await updater.DownloadAndApplyAsync(result, progress);
                if (ok)
                {
                    // Austausch-Skript laeuft und wartet auf das Prozessende —
                    // App jetzt beenden, sonst haengt es bei „Update laedt: 100 %".
                    UpdateStatusText.Text = "Update wird installiert — Anwendung startet neu …";
                    UpdateService.TerminateForUpdate();
                }
                else
                    UpdateStatusText.Text = "Self-Update nicht moeglich — Release-Seite oeffnen.";
            }
            else if (dlg.Result == UpdateDialogResult.Later)
            {
                UpdateStatusText.Text = $"Update auf {result.Latest} wird später erinnert.";
                UpdateCheckButton.IsEnabled = true;
            }
            else
            {
                UpdateStatusText.Text = $"Update auf {result.Latest} übersprungen.";
                UpdateCheckButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"Fehler: {ex.Message}";
            UpdateCheckButton.IsEnabled = true;
        }
    }
}
