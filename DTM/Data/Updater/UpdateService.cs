using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using NLog;
using SystemFile = System.IO.File;

namespace DTM.Updater;

/// <summary>
/// Update-Check + Self-Update gegen GitHub Releases (Muster: Klemmbrett/
/// Kroste-Standard). Ersetzt seit v2.3.0 den Samba-basierten Update-Weg —
/// funktioniert Cross-Platform (Windows-ZIP, Linux tar.gz, AppImage
/// inplace) ohne SMB-Abhaengigkeit.
///
/// Prinzipien:
/// - Nie silent installieren: User muss zustimmen (UpdatePromptWindow).
/// - Fehler nur Warn-Log — offline/Proxy stoert die App nicht.
/// - Max. 1 echter GitHub-Check pro App-Start (Cache); der manuelle
///   "Auf Updates pruefen"-Button im AboutWindow umgeht den Cache
///   ueber <c>forceRefresh=true</c>.
/// - Proxy-aware HttpClient (DefaultProxy + Negotiate) — laeuft
///   identisch am Arbeitsplatz und zuhause.
/// - Release-Notes werden aus dem Raw-File des Repos geladen
///   (release-notes.json auf main), unabhaengig vom Release-Bundle.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    private const string ReleasesUrl =
        "https://api.github.com/repos/Kroste/DTM/releases/latest";
    private const string ReleaseNotesRawUrl =
        "https://raw.githubusercontent.com/Kroste/DTM/main/release-notes.json";

    private readonly HttpClient _http;
    private UpdateCheckResult? _cached;

    public UpdateService()
    {
        var handler = new HttpClientHandler
        {
            Proxy = WebRequest.DefaultWebProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DTM-UpdateCheck");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>
    /// Liest die laufende Version aus <c>AssemblyInformationalVersion</c>.
    /// MinVer erzeugt zwischen Tags Suffixe wie „2.0.1-alpha.0.5+sha" —
    /// die werden vor dem Parse abgeschnitten. Fehlende Build-Stelle wird
    /// auf 0 normalisiert, damit „1.2" nicht faelschlich als aelter als
    /// „1.2.0" gilt.
    /// </summary>
    public static Version CurrentVersion() =>
        ParseInformationalVersion(
            Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion);

    /// <summary>
    /// Tag wie „v1.2.3", „1.2.3+abc123" oder „1.2.3-alpha.1" → <see cref="Version"/>.
    /// Fallback bei nicht parsbaren Werten: <c>1.0.0</c>.
    /// </summary>
    public static Version ParseInformationalVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return new Version(1, 0, 0);
        var s = tag.Trim().TrimStart('v', 'V');
        var cut = s.IndexOfAny(['+', '-']);
        if (cut >= 0) s = s[..cut];
        if (!Version.TryParse(s, out var v)) return new Version(1, 0, 0);
        return v.Build < 0 ? new Version(v.Major, v.Minor, 0) : v;
    }

    /// <summary>
    /// Prueft GitHub auf ein neueres Release. Bei <paramref name="forceRefresh"/>=true
    /// wird der Cache umgangen — nutzt der manuelle "Auf Updates pruefen"-
    /// Button aus dem AboutWindow. Bei false: max. 1 echter Check pro
    /// App-Start.
    /// </summary>
    public async Task<UpdateCheckResult?> CheckForUpdateAsync(
        bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cached is not null) return _cached;

        var sw = Stopwatch.StartNew();
        try
        {
            _logger.Debug("Update-Check: {0}", ReleasesUrl);
            using var response = await _http.GetAsync(ReleasesUrl, ct);
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var root = json.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagEl))
            {
                _logger.Warn("Update-Check: GitHub-Antwort ohne 'tag_name'.");
                return null;
            }
            var tag = tagEl.GetString();
            var latest = ParseInformationalVersion(tag);
            if (latest == new Version(1, 0, 0) && !string.Equals(tag, "v1.0.0", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warn("Update-Check: Tag '{0}' nicht parsbar.", tag);
                return null;
            }

            string releaseUrl = root.TryGetProperty("html_url", out var urlEl)
                ? urlEl.GetString() ?? string.Empty : string.Empty;
            var (assetName, assetUrl) = SelectAsset(root);

            var current = CurrentVersion();
            _cached = new UpdateCheckResult(current, latest, latest > current,
                releaseUrl, assetName, assetUrl);
            _logger.Info("Update-Check fertig in {0} ms: aktuell {1}, neueste {2}, Update: {3}, Asset: {4}",
                sw.ElapsedMilliseconds, current, latest, _cached.UpdateAvailable, assetName ?? "—");
            return _cached;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Update-Check fehlgeschlagen nach {0} ms (offline/Proxy?)", sw.ElapsedMilliseconds);
            return null;
        }
    }

    /// <summary>
    /// Laedt <c>release-notes.json</c> als Raw-File aus dem Repo und
    /// filtert die Eintraege im Bereich (currentVersion, newVersion].
    /// Sortiert absteigend. Leere Liste bei Fehler / Datei fehlt.
    /// </summary>
    public async Task<IReadOnlyList<ReleaseNote>> LoadReleaseNotesAsync(
        Version currentVersion, Version newVersion, CancellationToken ct = default)
    {
        try
        {
            var notes = await _http.GetFromJsonAsync<List<ReleaseNote>>(
                ReleaseNotesRawUrl,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                ct);
            if (notes is null) return Array.Empty<ReleaseNote>();

            return notes
                .Where(n => Version.TryParse(n.Version, out var v) && v > currentVersion && v <= newVersion)
                .OrderByDescending(n => Version.Parse(n.Version))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "release-notes.json konnte nicht gelesen werden ({0}).", ReleaseNotesRawUrl);
            return Array.Empty<ReleaseNote>();
        }
    }

    /// <summary>
    /// Waehlt aus den Release-Assets das plattformpassende:
    /// win-x64.zip unter Windows, x86_64.AppImage bzw. linux-x64.tar.gz
    /// unter Linux. Order: AppImage bevorzugt (inplace-Update), sonst tar.gz.
    /// </summary>
    private static (string? name, string? url) SelectAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return (null, null);

        bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // Erst nach dem "besten" Match suchen (AppImage bei Linux), dann nach Fallback.
        (string? name, string? url) best = (null, null);
        (string? name, string? url) fallback = (null, null);

        foreach (var a in assets.EnumerateArray())
        {
            if (!a.TryGetProperty("name", out var nameEl)) continue;
            var name = nameEl.GetString();
            if (name is null) continue;
            if (!a.TryGetProperty("browser_download_url", out var urlEl)) continue;
            var url = urlEl.GetString();
            if (url is null) continue;

            if (isWin)
            {
                if (name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    best = (name, url);
                    break;
                }
            }
            else
            {
                if (name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
                    best = (name, url);
                else if (name.Contains("linux-x64", StringComparison.OrdinalIgnoreCase) &&
                         name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                    fallback = (name, url);
            }
        }
        return best.name is not null ? best : fallback;
    }

    /// <summary>
    /// Laedt das Update-Asset, entpackt es und startet einen Austausch-
    /// Prozess der die App beendet, Dateien ersetzt, neu startet.
    /// Rueckgabe: <c>false</c> wenn kein Self-Update moeglich (dann sollte
    /// der Aufrufer die Release-Seite oeffnen).
    /// </summary>
    public async Task<bool> DownloadAndApplyAsync(UpdateCheckResult update,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (update.AssetUrl is null || update.AssetName is null)
        {
            _logger.Warn("Kein passendes Update-Asset fuer diese Plattform — Self-Update nicht moeglich.");
            return false;
        }

        try
        {
            var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var work = Path.Combine(Path.GetTempPath(), "DTM-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            var assetPath = Path.Combine(work, update.AssetName);

            _logger.Info("Lade Update herunter: {0}", update.AssetName);
            await DownloadWithProgressAsync(update.AssetUrl, assetPath, progress, ct);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return ApplyWindows(assetPath, work, appDir);
            return ApplyLinux(assetPath, appDir);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Self-Update fehlgeschlagen.");
            return false;
        }
    }

    private async Task DownloadWithProgressAsync(string url, string dest,
        IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = SystemFile.Create(dest);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Report((double)read / total);
        }
        _logger.Debug("Download fertig: {0} Bytes.", read);
    }

    /// <summary>
    /// Windows: ZIP daneben entpacken, .bat schreibt nach App-Ende die
    /// Dateien um und startet neu. Wait-Process auf PID (zuverlaessiger
    /// als tasklist-Schleife). Batch-Zeilen OHNE fuehrende Einrueckung —
    /// eingerueckte Labels sind fuer cmd.exe kein gueltiges Sprungziel.
    ///
    /// Environment.Exit(0) wuerde die Finalizer des eingebetteten PS-SDK-
    /// Runspace aufrufen und damit unbegrenzt blockieren. Process.Kill()
    /// schickt direkt TerminateProcess/SIGKILL — kein Finalizer, kein Hang.
    /// </summary>
    private bool ApplyWindows(string zipPath, string work, string appDir)
    {
        var extract = Path.Combine(work, "extracted");
        ZipFile.ExtractToDirectory(zipPath, extract);

        var pid = Environment.ProcessId;
        var exe = Path.Combine(appDir, "DTM.exe");
        var bat = Path.Combine(work, "apply.bat");
        var log = Path.Combine(work, "update.log");

        var lines = new[]
        {
            "@echo off",
            $"echo Warte auf Prozess {pid} >\"{log}\"",
            $"powershell -NoProfile -Command \"try {{ Wait-Process -Id {pid} -ErrorAction Stop }} catch {{}}\" >>\"{log}\" 2>&1",
            "ping 127.0.0.1 -n 2 >NUL",
            $"echo Kopiere Dateien >>\"{log}\"",
            $"xcopy /E /Y /I /Q \"{extract}\\*\" \"{appDir}\\\" >>\"{log}\" 2>&1",
            $"echo Starte neu >>\"{log}\"",
            $"start \"\" \"{exe}\"",
        };
        SystemFile.WriteAllLines(bat, lines);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{bat}\"\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = work
        });
        _logger.Info("Windows-Update vorbereitet ({0}) — App wird beendet fuer den Austausch.", bat);
        return true;
    }

    /// <summary>
    /// Linux: AppImage ersetzt sich selbst (eine Datei — cp/mv statt rm+cp
    /// wegen Loop-Device-Lock); tar.gz wird ins App-Verzeichnis entpackt.
    /// Wait-Loop per <c>kill -0</c> auf PID, danach setsid neu starten.
    /// </summary>
    private bool ApplyLinux(string assetPath, string appDir)
    {
        var runningAppImage = Environment.GetEnvironmentVariable("APPIMAGE");
        var pid = Environment.ProcessId;
        var sh = Path.Combine(Path.GetTempPath(), $"dtm-update-{Guid.NewGuid():N}.sh");

        string body;
        if (assetPath.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase) && runningAppImage is not null)
        {
            body = string.Join('\n',
                "#!/bin/sh",
                $"while kill -0 {pid} 2>/dev/null; do sleep 1; done",
                "sleep 1",
                $"chmod +x '{assetPath}'",
                $"cp -f '{assetPath}' '{runningAppImage}' || mv -f '{assetPath}' '{runningAppImage}'",
                $"rm -f '{assetPath}'",
                $"setsid '{runningAppImage}' >/dev/null 2>&1 &");
        }
        else if (assetPath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            var exe = Path.Combine(appDir, "DTM");
            body = string.Join('\n',
                "#!/bin/sh",
                $"while kill -0 {pid} 2>/dev/null; do sleep 1; done",
                "sleep 1",
                $"tar -xzf '{assetPath}' -C '{appDir}'",
                $"chmod +x '{exe}'",
                $"setsid '{exe}' >/dev/null 2>&1 &");
        }
        else
        {
            _logger.Warn("Linux-Update: unerwartetes Asset ({0}) oder kein laufendes AppImage.",
                Path.GetFileName(assetPath));
            return false;
        }

        SystemFile.WriteAllText(sh, body);
        Process.Start(new ProcessStartInfo("/bin/sh", $"\"{sh}\"") { UseShellExecute = false });
        _logger.Info("Linux-Update vorbereitet — App wird beendet fuer den Austausch.");
        return true;
    }

    public void Dispose() => _http.Dispose();
}

public sealed record UpdateCheckResult(
    Version Current,
    Version Latest,
    bool UpdateAvailable,
    string ReleaseUrl,
    string? AssetName = null,
    string? AssetUrl = null);
