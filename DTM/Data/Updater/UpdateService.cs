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
/// Update-Check + Self-Update. Zwei Kanaltypen, erkannt an der Schreibweise
/// der Einstellung <c>UpdateChannel</c> (siehe <see cref="UpdateChannel"/>):
///
/// <list type="bullet">
/// <item><b>Ordner im Netz</b> (Standard) — das Rollout-Verzeichnis
///       <c>\\samba01\…\MS-SQL\DTM</c>. Kein Proxy, kein Internetzugang; das
///       Ausrollen ist ein Kopiervorgang. Seit 2026-08-25 der Regelweg, weil
///       GitHub aus dem Firmennetz nicht mehr erreichbar ist.</item>
/// <item><b>GitHub Releases</b> — greift, sobald in der Einstellung eine
///       <c>https://</c>-Adresse steht. Fuer Entwicklung ausserhalb des
///       Firmennetzes.</item>
/// </list>
///
/// Prinzipien (unveraendert fuer beide Kanaele):
/// - Nie silent installieren: User muss zustimmen (UpdatePromptWindow).
/// - Fehler nur Warn-Log — offline/Proxy/fehlendes Netzlaufwerk stoeren die
///   App nicht.
/// - Max. 1 echter Check pro App-Start (Cache); der manuelle
///   "Auf Updates pruefen"-Button im AboutWindow umgeht den Cache
///   ueber <c>forceRefresh=true</c>.
/// - Proxy-aware HttpClient (DefaultProxy + Negotiate) fuer den GitHub-Weg.
/// - Das Paket wird auch vom Share erst in den Temp-Ordner kopiert und dann
///   entpackt, nie direkt vom Netzlaufwerk (siehe <see cref="FetchAsync"/>).
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    private const string ReleasesUrl =
        "https://api.github.com/repos/LHP542/DTM/releases/latest";
    private const string ReleaseNotesRawUrl =
        "https://raw.githubusercontent.com/LHP542/DTM/main/release-notes.json";

    private readonly Lazy<HttpClient> _http;
    private readonly string _channel;
    private readonly bool _isWindows;
    private UpdateCheckResult? _cached;

    /// <param name="channel">
    /// Update-Quelle. Leer = <see cref="UpdateChannel.DefaultFolder"/>.
    /// Wird ueblicherweise aus <c>AppSettingsStore.LoadFocSql().UpdateChannel</c>
    /// gespeist; als Parameter, damit Tests einen Temp-Ordner setzen koennen.
    /// </param>
    /// <param name="isWindows">
    /// Bestimmt, nach welchem Paketformat gesucht wird. Injizierbar, damit
    /// beide Plattform-Zweige unabhaengig vom Test-Host geprueft werden
    /// koennen — dieselbe Ueberlegung wie bei <see cref="SelectAsset"/>.
    /// </param>
    public UpdateService(string? channel = null, bool? isWindows = null)
    {
        _channel = UpdateChannel.Resolve(channel);
        _isWindows = isWindows ?? RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // Lazy: der Ordner-Kanal braucht keinen HttpClient. Ihn trotzdem im
        // Konstruktor zu bauen hiesse, bei jedem Start einen Proxy-Handler mit
        // Windows-Integrated-Credentials aufzusetzen — auf Nicht-Windows
        // unnoetig und je nach Laufzeit nicht unterstuetzt.
        _http = new Lazy<HttpClient>(() =>
        {
            var handler = new HttpClientHandler
            {
                Proxy = WebRequest.DefaultWebProxy,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DTM-UpdateCheck");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        });

        _logger.Debug("Update-Kanal: {0} ({1})", _channel,
            UpdateChannel.LooksLikeFolder(_channel) ? "Ordner" : "GitHub");
    }

    /// <summary>Der aktive Kanal — fuer Anzeige und Diagnose.</summary>
    public string Channel => _channel;

    /// <summary><c>true</c>, wenn der Kanal ein Ordner ist (kein GitHub).</summary>
    public bool UsesFolderChannel => UpdateChannel.LooksLikeFolder(_channel);

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

        if (UsesFolderChannel) return CheckFolder();

        var sw = Stopwatch.StartNew();
        try
        {
            _logger.Debug("Update-Check: {0}", ReleasesUrl);
            using var response = await _http.Value.GetAsync(ReleasesUrl, ct);
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
            var (assetName, assetUrl) = SelectAsset(
                root, _isWindows);

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
    /// Prueft den Ordner-Kanal. Laeuft synchron — es sind ein
    /// <c>Directory.Exists</c> und ein Verzeichnis-Listing, kein Netz-Roundtrip
    /// wie bei GitHub.
    /// </summary>
    private UpdateCheckResult? CheckFolder()
    {
        var sw = Stopwatch.StartNew();

        (string? path, Version? latest) = UpdateChannel.FindNewestPackage(
            _channel, _isWindows);

        if (path is null || latest is null)
        {
            // Kein Paket oder Ordner nicht erreichbar — beides bereits in
            // UpdateChannel geloggt (Debug). Kein Cache: beim naechsten
            // manuellen Versuch soll erneut geschaut werden.
            return null;
        }

        var current = CurrentVersion();
        bool available = UpdateChannel.Normalize(latest) > UpdateChannel.Normalize(current);

        _cached = new UpdateCheckResult(
            current, latest, available,
            // "Release-Seite oeffnen" oeffnet den Ordner im Explorer — der
            // sinnvollste Ersatz, wenn es keine Release-Seite gibt.
            ReleaseUrl: _channel,
            AssetName: Path.GetFileName(path),
            AssetUrl: path);

        _logger.Info("Update-Check (Ordner) fertig in {0} ms: aktuell {1}, neueste {2}, Update: {3}, Paket: {4}",
            sw.ElapsedMilliseconds, current, latest, available, Path.GetFileName(path));
        return _cached;
    }

    /// <summary>
    /// Laedt <c>release-notes.json</c> und filtert die Eintraege im Bereich
    /// (currentVersion, newVersion]. Sortiert absteigend. Leere Liste bei
    /// Fehler oder fehlender Datei — fehlende Notizen sind kein Grund, ein
    /// Update zu verschweigen.
    ///
    /// <para>Quelle ist der aktive Kanal: die Datei neben den Paketen im
    /// Ordner bzw. das Raw-File im Repo.</para>
    /// </summary>
    public async Task<IReadOnlyList<ReleaseNote>> LoadReleaseNotesAsync(
        Version currentVersion, Version newVersion, CancellationToken ct = default)
    {
        string source = UsesFolderChannel
            ? Path.Combine(_channel, UpdateChannel.ReleaseNotesFileName)
            : ReleaseNotesRawUrl;

        try
        {
            List<ReleaseNote>? notes;
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            if (UsesFolderChannel)
            {
                if (!SystemFile.Exists(source))
                {
                    _logger.Debug("Keine {0} im Update-Ordner.", UpdateChannel.ReleaseNotesFileName);
                    return Array.Empty<ReleaseNote>();
                }
                await using var stream = SystemFile.OpenRead(source);
                notes = await JsonSerializer.DeserializeAsync<List<ReleaseNote>>(stream, options, ct);
            }
            else
            {
                notes = await _http.Value.GetFromJsonAsync<List<ReleaseNote>>(source, options, ct);
            }

            if (notes is null) return Array.Empty<ReleaseNote>();

            return notes
                .Where(n => Version.TryParse(n.Version, out var v) && v > currentVersion && v <= newVersion)
                .OrderByDescending(n => Version.Parse(n.Version))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "release-notes.json konnte nicht gelesen werden ({0}).", source);
            return Array.Empty<ReleaseNote>();
        }
    }

    /// <summary>
    /// Waehlt aus den Release-Assets das plattformpassende:
    /// .zip unter Windows, .AppImage bzw. .tar.gz unter Linux. Order:
    /// AppImage bevorzugt (inplace-Update), sonst tar.gz.
    ///
    /// Toleriert bewusst BEIDE Namensschemata: den Kroste-Standard
    /// (<c>…-win-x64.zip</c> / <c>…-linux-x64.tar.gz</c>) UND die tatsaechlich
    /// von DTMs release.yml erzeugten Namen (<c>…-windows.zip</c> /
    /// <c>…-linux.tar.gz</c>). Frueher matchte der Selector nur die harten
    /// Strings „win-x64"/„linux-x64" — dadurch fand er das real hochgeladene
    /// „DTM-vX.Y.Z-windows.zip" NICHT, <see cref="DownloadAndApplyAsync"/>
    /// bekam kein Asset und Self-Update war unter Windows generell unmoeglich
    /// (Statusleiste „Self-Update nicht moeglich"). AppImage matchte weiter,
    /// weshalb es unter Linux nie auffiel.
    ///
    /// <paramref name="isWindows"/> wird injiziert (statt hier via
    /// <see cref="RuntimeInformation"/> ermittelt), damit beide
    /// Plattform-Zweige deterministisch und OS-unabhaengig testbar sind.
    /// </summary>
    internal static (string? name, string? url) SelectAsset(JsonElement release, bool isWindows)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return (null, null);

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

            if (isWindows)
            {
                // Windows-Build ist immer ein .zip; im Namen entweder „win-x64"
                // (Standard) oder „windows" (DTM-release.yml).
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                    (name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("windows", StringComparison.OrdinalIgnoreCase)))
                {
                    best = (name, url);
                    break;
                }
            }
            else
            {
                if (name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
                    best = (name, url);
                else if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) &&
                         name.Contains("linux", StringComparison.OrdinalIgnoreCase))
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

            _logger.Info("Hole Update-Paket: {0}", update.AssetName);
            await FetchAsync(update.AssetUrl, assetPath, progress, ct);

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

    /// <summary>
    /// Beendet den laufenden Prozess, nachdem <see cref="DownloadAndApplyAsync"/>
    /// das Austausch-Skript gestartet hat. Der Installer (Windows:
    /// <c>Wait-Process</c>, Linux: <c>kill -0</c>) wartet genau auf dieses
    /// Prozessende, um die Dateien zu ersetzen und neu zu starten — OHNE diesen
    /// Aufruf laeuft die App weiter und bleibt bei „Update laedt: 100 %" haengen.
    ///
    /// Bewusst <see cref="Process"/>.<c>Kill()</c> statt
    /// <see cref="Environment"/>.<c>Exit(0)</c>: Exit ruft die Finalizer des
    /// eingebetteten PowerShell-SDK-Runspace auf und blockiert damit unbegrenzt.
    /// Kill schickt direkt TerminateProcess/SIGKILL — keine Finalizer, kein Hang.
    /// Der Austausch muss nicht sauber beendet werden; der Installer wartet nur
    /// auf das Verschwinden der PID. Environment.Exit nur als letzter Fallback,
    /// falls Kill wider Erwarten wirft.
    /// </summary>
    public static void TerminateForUpdate()
    {
        try
        {
            _logger.Info("Update vorbereitet — Prozess wird fuer den Austausch beendet (Kill).");
            using var self = Process.GetCurrentProcess();
            self.Kill();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Process.Kill fuer Self-Update fehlgeschlagen — Fallback Environment.Exit(0).");
            Environment.Exit(0);
        }
    }

    /// <summary>
    /// Holt das Paket — per HTTP oder aus dem Ordner.
    ///
    /// <para><b>Auch vom Share wird kopiert, nicht direkt entpackt.</b> Zwei
    /// Gruende: Das Paket koennte zwischen Pruefung und Entpacken ausgetauscht
    /// werden, und ein Netzlaufwerk, das mitten im Entpacken wegbricht, wuerde
    /// ein halb ersetztes Programmverzeichnis hinterlassen.</para>
    /// </summary>
    private async Task FetchAsync(string source, string dest,
        IProgress<double>? progress, CancellationToken ct)
    {
        if (!UpdateChannel.LooksLikeFolder(source))
        {
            await DownloadWithProgressAsync(source, dest, progress, ct);
            return;
        }

        await using var src = SystemFile.OpenRead(source);
        await using var dst = SystemFile.Create(dest);
        long total = src.Length;
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Report((double)read / total);
        }
        _logger.Debug("Vom Update-Ordner kopiert: {0} Bytes.", read);
    }

    private async Task DownloadWithProgressAsync(string url, string dest,
        IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await _http.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
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

    public void Dispose()
    {
        // Nur wegwerfen, was auch gebaut wurde — beim Ordner-Kanal entsteht
        // nie ein HttpClient.
        if (_http.IsValueCreated) _http.Value.Dispose();
    }
}

public sealed record UpdateCheckResult(
    Version Current,
    Version Latest,
    bool UpdateAvailable,
    string ReleaseUrl,
    string? AssetName = null,
    string? AssetUrl = null);
