using System.Diagnostics;
using System.Text;
using DTM.Config;
using DTM.MariaDb;
using NLog;
using SystemFile = System.IO.File;

namespace DTM.Data.MariaDb;

/// <summary>Ein gefundener Dump im Backup-Verzeichnis.</summary>
/// <param name="Path">Voller Pfad.</param>
/// <param name="FileName">Dateiname zur Anzeige.</param>
/// <param name="Created">Zeitpunkt der Erstellung.</param>
/// <param name="SizeBytes">Größe in Byte — die Aufbereitung für die
/// Anzeige macht die UI, damit hier nichts vorab gerundet wird.</param>
public sealed record MariaDbBackupFile(string Path, string FileName, DateTime Created, long SizeBytes);

/// <summary>
/// Backup und Restore einer MariaDB-Datenbank über die externen Werkzeuge
/// <c>mariadb-dump</c> und <c>mariadb</c>.
///
/// <para><b>Warum überhaupt externe Werkzeuge:</b> MariaDB kennt kein
/// <c>BACKUP DATABASE</c> wie MSSQL. Ein vollständiger, wieder einspielbarer
/// Dump entsteht nur über das Kommandozeilenwerkzeug — <c>SELECT … INTO
/// OUTFILE</c> schreibt serverseitig, pro Tabelle und ohne Schema und ist
/// deshalb kein Ersatz.</para>
///
/// <para><b>Das Passwort steht nie auf der Kommandozeile.</b> Argumente eines
/// Prozesses sind auf dem Rechner für jeden lesbar, der die Prozessliste
/// sehen darf — <c>--password=geheim</c> wäre damit im Klartext sichtbar.
/// DTM schreibt es stattdessen in eine temporäre Optionsdatei und übergibt
/// sie als <c>--defaults-extra-file</c>; die Datei wird unter Unix auf 0600
/// gesetzt und in jedem Fall wieder gelöscht. Das ist der von MariaDB dafür
/// vorgesehene Weg.</para>
/// </summary>
public sealed class MariaDbBackupService(MariaDb_Connector connector, MariaDbSettings settings)
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    private readonly MariaDb_Connector _connector = connector;
    private readonly MariaDbSettings _settings = settings;

    /// <summary>Kandidaten für das Dump-Werkzeug, neuer Name zuerst.</summary>
    private static readonly string[] DumpCandidates = ["mariadb-dump", "mysqldump"];

    /// <summary>Kandidaten für den Client (Restore), neuer Name zuerst.</summary>
    private static readonly string[] ClientCandidates = ["mariadb", "mysql"];

    /// <summary>
    /// Zielverzeichnis für diese Datenbank:
    /// <c>&lt;Wurzel&gt;\&lt;Server&gt;\&lt;Datenbank&gt;</c>. Pro Server ein
    /// eigener Zweig, damit gleichnamige Datenbanken auf verschiedenen Servern
    /// nicht im selben Ordner landen.
    /// </summary>
    public string BackupDirectoryFor(string database)
    {
        string root = string.IsNullOrWhiteSpace(_settings.BackupRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "DTM-Backups", "MariaDB")
            : _settings.BackupRoot;

        (string host, _) = _connector.HostAndPort();
        return Path.Combine(root, SanitizeForPath(host), SanitizeForPath(database));
    }

    /// <summary>Vorhandene Dumps, neueste zuerst.</summary>
    public IReadOnlyList<MariaDbBackupFile> ListBackups(string database)
    {
        string dir = BackupDirectoryFor(database);
        if (!Directory.Exists(dir)) return [];

        return Directory.EnumerateFiles(dir, "*.sql")
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new MariaDbBackupFile(f.FullName, f.Name, f.CreationTime, f.Length))
            .ToList();
    }

    /// <summary>
    /// Schreibt einen vollständigen Dump und liefert den Pfad zurück.
    /// Fortschritt und Meldungen des Werkzeugs gehen laufend an
    /// <paramref name="onInfo"/>.
    /// </summary>
    public async Task<string> BackupAsync(
        string database, Action<string>? onInfo = null, CancellationToken ct = default)
    {
        string tool = ResolveTool(_settings.DumpPath, DumpCandidates, "mariadb-dump");
        string dir = BackupDirectoryFor(database);
        Directory.CreateDirectory(dir);

        string target = Path.Combine(dir, $"{SanitizeForPath(database)}-{DateTime.Now:yyyyMMdd_HHmm}.sql");
        (string host, uint port) = _connector.HostAndPort();

        // Vor dem Dump prüfen, ob das Werkzeug zu DTM und zum Server passt.
        // Die Meldung geht als Hinweis durch, sie bricht nicht ab: ein Dump mit
        // einem zu alten Werkzeug ist immer noch besser als gar keiner, und wer
        // gerade sichern will, soll nicht von einer Versionsfrage aufgehalten
        // werden. Gemeldet wird trotzdem deutlich — der Fall, der weh tut, ist
        // ein Dump, der Erfolg meldet und sich später nicht einspielen lässt.
        string? versionsHinweis = await CheckToolVersionAsync(tool, ct).ConfigureAwait(false);
        if (versionsHinweis is not null)
        {
            onInfo?.Invoke(versionsHinweis);
            _logger.Warn("MariaDB-Werkzeug: {0}", versionsHinweis);
        }

        onInfo?.Invoke($"Backup von '{database}' nach {target}");
        _logger.Info("MariaDB-Backup: {0} → {1}", database, target);

        // --single-transaction: konsistenter Stand ohne die Tabellen zu
        // sperren (gilt für transaktionale Engines wie InnoDB).
        // --routines/--events/--triggers: sonst fehlen sie im Dump und der
        // Restore liefert eine unvollständige Datenbank.
        List<string> args =
        [
            $"--host={host}",
            $"--port={port}",
            $"--user={_connector.CredentialRef.User}",
            "--single-transaction",
            "--routines",
            "--events",
            "--triggers",
            "--default-character-set=utf8mb4",
            $"--result-file={target}",
            database,
        ];

        int exitCode = await RunToolAsync(tool, args, onInfo, ct);
        if (exitCode != 0)
        {
            // Eine halb geschriebene Datei ist schlimmer als keine: sie sieht
            // aus wie ein Backup und lässt sich nicht einspielen.
            TryDelete(target);
            throw new InvalidOperationException(
                $"{Path.GetFileName(tool)} endete mit Code {exitCode}. Die unvollständige Datei wurde entfernt.");
        }

        var info = new FileInfo(target);
        onInfo?.Invoke($"Backup fertig: {info.Name} ({info.Length / 1024d / 1024d:N1} MB)");
        return target;
    }

    /// <summary>
    /// Spielt einen Dump in die Datenbank zurück. <b>Destruktiv</b> — der
    /// Aufrufer muss vorher bestätigen lassen.
    /// </summary>
    public async Task RestoreAsync(
        string database, string backupFile,
        Action<string>? onInfo = null, CancellationToken ct = default)
    {
        if (!SystemFile.Exists(backupFile))
            throw new FileNotFoundException($"Dump nicht gefunden: {backupFile}", backupFile);

        string tool = ResolveTool(_settings.ClientPath, ClientCandidates, "mariadb");
        (string host, uint port) = _connector.HostAndPort();

        onInfo?.Invoke($"Spiele {Path.GetFileName(backupFile)} in '{database}' ein …");
        _logger.Info("MariaDB-Restore: {0} → {1}", backupFile, database);

        List<string> args =
        [
            $"--host={host}",
            $"--port={port}",
            $"--user={_connector.CredentialRef.User}",
            "--default-character-set=utf8mb4",
            database,
        ];

        // Der Client liest das Skript von stdin — es gibt keine Option dafür.
        int exitCode = await RunToolAsync(tool, args, onInfo, ct, stdinFile: backupFile);
        if (exitCode != 0)
            throw new InvalidOperationException(
                $"{Path.GetFileName(tool)} endete mit Code {exitCode}. Die Datenbank kann unvollständig sein.");

        onInfo?.Invoke("Restore abgeschlossen.");
    }

    /// <summary>
    /// Ruft <c>--version</c> auf dem Dump-Werkzeug auf und vergleicht das
    /// Ergebnis mit der Untergrenze und mit der Server-Version. Liefert den
    /// Meldungstext oder <c>null</c>, wenn alles passt.
    ///
    /// <para>Das Ergebnis wird für die Lebensdauer des Dienstes gemerkt: das
    /// Werkzeug wechselt während einer Sitzung nicht, und ein Prozessstart pro
    /// Sicherung wäre verschenkte Zeit.</para>
    /// </summary>
    private async Task<string?> CheckToolVersionAsync(string tool, CancellationToken ct)
    {
        if (_versionsHinweisGeprueft) return _versionsHinweis;

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = tool,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add("--version");

            using Process p = new() { StartInfo = psi };
            p.Start();
            string ausgabe = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);

            DumpToolInfo? info = MariaDbToolVersion.Parse(ausgabe);
            string? serverVersion = _connector.TryGetServerVersion();
            _versionsHinweis = MariaDbToolVersion.Check(info, serverVersion, tool);

            if (_versionsHinweis is null && info is not null)
                _logger.Info("MariaDB-Werkzeug passt: {0} (Server: {1})", info.RawOutput, serverVersion ?? "unbekannt");
        }
        catch (Exception ex)
        {
            // Eine gescheiterte Versionsabfrage darf die Sicherung nicht
            // verhindern — sie ist eine Zusatzprüfung, keine Voraussetzung.
            _logger.Warn(ex, "Versionsabfrage von '{0}' fehlgeschlagen.", tool);
            _versionsHinweis = null;
        }

        _versionsHinweisGeprueft = true;
        return _versionsHinweis;
    }

    private bool _versionsHinweisGeprueft;
    private string? _versionsHinweis;

    /// <summary>
    /// Startet ein Werkzeug, streamt dessen Ausgaben und wartet auf das Ende.
    /// Das Passwort geht über eine temporäre Optionsdatei, nie als Argument.
    /// </summary>
    private async Task<int> RunToolAsync(
        string tool, IReadOnlyList<string> args, Action<string>? onInfo,
        CancellationToken ct, string? stdinFile = null)
    {
        string optionsFile = WriteCredentialFile();
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = tool,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = stdinFile is not null,
                RedirectStandardError = true,
                RedirectStandardInput = stdinFile is not null,
                StandardErrorEncoding = Encoding.UTF8,
            };

            // Muss das erste Argument sein — spätere Optionen sollen die
            // Datei überschreiben können, nicht umgekehrt.
            psi.ArgumentList.Add($"--defaults-extra-file={optionsFile}");
            foreach (string a in args) psi.ArgumentList.Add(a);

            using Process process = new() { StartInfo = psi };
            process.Start();

            // stderr trägt bei diesen Werkzeugen auch die Fortschritts- und
            // Warnmeldungen, nicht nur Fehler.
            Task<string> stderr = process.StandardError.ReadToEndAsync(ct);

            if (stdinFile is not null)
            {
                await using FileStream input = SystemFile.OpenRead(stdinFile);
                await input.CopyToAsync(process.StandardInput.BaseStream, ct);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(ct);
            string errorText = await stderr;

            foreach (string line in errorText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                // Der Hinweis auf die Passwortdatei ist erwartbar und kein
                // Problem — er würde nur verunsichern.
                if (trimmed.Contains("Using a password on the command line", StringComparison.OrdinalIgnoreCase))
                    continue;
                onInfo?.Invoke($"  {trimmed}");
            }

            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"'{tool}' ließ sich nicht starten: {ex.Message}. "
                + "Pfad in den Einstellungen unter MariaDB prüfen.", ex);
        }
        finally
        {
            TryDelete(optionsFile);
        }
    }

    /// <summary>
    /// Schreibt eine temporäre Optionsdatei mit dem Passwort und schützt sie
    /// so weit die Plattform es zulässt.
    /// </summary>
    private string WriteCredentialFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dtm-mariadb-{Guid.NewGuid():N}.cnf");

        // Datei zuerst leer anlegen, Rechte setzen, dann erst das Passwort
        // hineinschreiben — sonst steht es kurzzeitig in einer Datei mit den
        // Standardrechten des Verzeichnisses.
        SystemFile.WriteAllText(path, string.Empty);
        if (!OperatingSystem.IsWindows())
            SystemFile.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        string password = _connector.CredentialRef.Password ?? string.Empty;
        SystemFile.WriteAllText(path,
            $"[client]{Environment.NewLine}password=\"{password.Replace("\"", "\\\"", StringComparison.Ordinal)}\"{Environment.NewLine}");
        return path;
    }

    /// <summary>
    /// Ermittelt den Pfad zum Werkzeug: konfigurierter Wert, sonst die
    /// bekannten Namen aus dem <c>PATH</c>.
    /// </summary>
    internal static string ResolveTool(string configured, IReadOnlyList<string> candidates, string displayName)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (SystemFile.Exists(configured)) return configured;
            throw new FileNotFoundException(
                $"Der in den Einstellungen hinterlegte Pfad zu {displayName} existiert nicht: {configured}",
                configured);
        }

        foreach (string candidate in candidates)
        {
            string? found = FindOnPath(candidate);
            if (found is not null) return found;
        }

        throw new FileNotFoundException(
            $"{displayName} wurde nicht gefunden. Entweder in den PATH aufnehmen "
            + $"oder den vollen Pfad in den Einstellungen unter MariaDB eintragen. "
            + $"Gesucht wurde nach: {string.Join(", ", candidates)}.");
    }

    private static string? FindOnPath(string command)
    {
        string[] extensions = OperatingSystem.IsWindows()
            ? [".exe", ".cmd", ".bat"]
            : [string.Empty];

        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string ext in extensions)
            {
                try
                {
                    string full = Path.Combine(dir.Trim(), command + ext);
                    if (SystemFile.Exists(full)) return full;
                }
                catch (ArgumentException)
                {
                    // Ungültige Zeichen in einem PATH-Eintrag — überspringen
                    // statt die ganze Suche scheitern zu lassen.
                }
            }
        }
        return null;
    }

    /// <summary>Ersetzt alles, was in einem Pfadsegment stören könnte.</summary>
    internal static string SanitizeForPath(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder sb = new(value.Length);
        foreach (char c in value) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (SystemFile.Exists(path)) SystemFile.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Temporäre Datei {0} ließ sich nicht entfernen.", path);
        }
    }
}
