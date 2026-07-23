using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Data;
using DTM.ODBC;
using DTM.Util;
using NLog;

namespace DTM.ORACLE
{

    public class ORACLE_ODBC(ServerCredential credential) : IDisposable, IDTM_ODBC
    {
        private readonly REST _rest = new REST(credential, true);
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        public void Dispose()
        {
            _rest?.Dispose();
        }

        public List<Database_Info> get_Datenbank_Names()
        {
            try
            {
                IReadOnlyList<VmFqdnEntry> _data = AsyncUtil.RunSync(() => _rest.GetAllVmFqdnsAsync());

                foreach (var v in _data)
                {
                    _logger.Debug($"{v.Name,-30} | {v.Status,-10} | {v.Fqdn ?? "(kein FQDN gemeldet)"} | {v.Id,-4}");
                }

                return _data.Select(x => new Database_Info
                {
                    Name = x.Name,
                    FQDN = x.Fqdn,
                    Status = string.Equals(x.Status, "up", StringComparison.OrdinalIgnoreCase) ? Database_Status.up : Database_Status.down,
                    Id = x.Id
                }).Where(x => !x.Name.StartsWith("TPL") && !x.Name.Contains("DBMANAGER")).OrderBy(x => x.FQDN).ToList();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex.StackTrace!);
                return new List<Database_Info>(0);
            }
        }

        public Database_Stats GetDatabase_Stats(Database_Info database)
        {
            var stats = new Database_Stats_ORACLE
            {
                DatabaseTyp = "ORACLE",
                Server = database.FQDN,
                HostName = database.FQDN,
                State = database.Status.ToString(),
                Sessions = new List<Session>(),
                Tablespaces = new List<Tablespace>()
            };
            var sqlB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(OracleStatsSql));
            var remoteCmd = $"echo '{sqlB64}' | base64 -d | sqlplus -s / as sysdba";
            var lines = ExecuteRemoteSqlScript(database.FQDN!, remoteCmd);

            if (lines.Length == 0)
            {
                _logger.Warn($"Keine Stats für '{database.FQDN}' erhalten – siehe vorherige Warnungen.");
                return stats; // Grundgerüst trotzdem zurückgeben
            }

            ParseOracleStats(lines, stats);
            return stats;
        }

        // Das SQL aus der PowerShell-Funktion 1:1 übernommen.
        // Bewusst als const, damit es im Code sichtbar bleibt – ggf. später in eine
        // Embedded-Resource (.sql-Datei) auslagern.
        private const string OracleStatsSql = @"
            SET PAGESIZE 0 FEEDBACK OFF HEADING OFF LINESIZE 500 TRIMSPOOL ON TAB OFF

            ALTER SESSION SET NLS_NUMERIC_CHARACTERS = '.,';

            SELECT 'DBSIZE|' || ROUND(SUM(bytes)/1024/1024, 2)
            FROM dba_data_files;

            SELECT 'SESSIONS|' || COUNT(*)
            FROM v$session
            WHERE type = 'USER';

            SELECT 'SESS|' || NVL(username, 'N/A') || '|' || NVL(machine, 'N/A') || '|' || NVL(program, 'N/A') || '|' || status
            FROM v$session
            WHERE type = 'USER'
            ORDER BY machine, username;

            SELECT 'SGA|' || ROUND(SUM(value)/1024/1024, 2)
            FROM v$sga;

            SELECT 'PGA|' || ROUND(value/1024/1024, 2)
            FROM v$pgastat
            WHERE name = 'total PGA allocated';

            SELECT 'ARCHIVELOG|' || log_mode
            FROM v$database;

            SELECT 'INSTANCE|' || status || '|' || instance_name || '|' || host_name || '|' || version
            FROM v$instance;

            SELECT 'TS|' ||
            df.tablespace_name || '|' ||
            ROUND(df.total_mb, 2) || '|' ||
            ROUND(df.total_mb - NVL(fs.free_mb, 0), 2) || '|' ||
            ROUND(NVL(fs.free_mb, 0), 2) || '|' ||
            ROUND((df.total_mb - NVL(fs.free_mb, 0)) * 100 / NULLIF(df.total_mb, 0), 1)
            FROM   (SELECT tablespace_name, SUM(bytes)/1024/1024 AS total_mb
            FROM   dba_data_files
            GROUP BY tablespace_name) df
            LEFT JOIN
                    (SELECT tablespace_name, SUM(bytes)/1024/1024 AS free_mb
                    FROM   dba_free_space
                    GROUP BY tablespace_name) fs
            ON     df.tablespace_name = fs.tablespace_name
            ORDER BY df.tablespace_name;

            EXIT;
        ";

        public string[] ExecuteRemoteSqlScript(string databaseName, string remoteCmd)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
                throw new ArgumentException("DatabaseName darf nicht leer sein.", nameof(databaseName));
            if (string.IsNullOrWhiteSpace(remoteCmd))
                throw new ArgumentException("SqlScript darf nicht leer sein.", nameof(remoteCmd));

            _logger.Debug($"Führe Remote-SQL auf oracle@{databaseName} aus: {remoteCmd}");

            var psi = new ProcessStartInfo
            {
                FileName = "ssh",
                UseShellExecute = false,
                RedirectStandardInput = true,   // <-- NEU: stdin schließen, damit ssh nicht wartet
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // --- SSH-Optionen für unbeaufsichtigten Betrieb -------------------------
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("BatchMode=yes");
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("StrictHostKeyChecking=accept-new");
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("ConnectTimeout=10");
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("ServerAliveInterval=15");
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("ServerAliveCountMax=2");
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("NumberOfPasswordPrompts=0");
            // Optional, wenn du den known_hosts-Pfad explizit kontrollieren willst:
            // psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(@"UserKnownHostsFile=C:\ProgramData\DTM\known_hosts");
            // -----------------------------------------------------------------------

            psi.ArgumentList.Add($"oracle@{databaseName}");
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(remoteCmd);

            StringBuilder stdout = new();
            StringBuilder stderr = new();

            try
            {
                using Process proc = new Process { StartInfo = psi };
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                proc.Start();
                proc.StandardInput.Close();           // <-- NEU: explizit zu, falls ssh stdin liest
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                if (!proc.WaitForExit(60_000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    _logger.Warn($"SSH zu oracle@{databaseName}: Timeout nach 60s");
                    return Array.Empty<string>();
                }
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                {
                    _logger.Warn($"SSH zu oracle@{databaseName} fehlgeschlagen " +
                                 $"(Exit {proc.ExitCode}): {stderr.ToString().Trim()}");
                    return Array.Empty<string>();
                }

                return stdout.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Fehler beim Remote-SQL auf oracle@{databaseName}: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        internal static void ParseOracleStats(string[] lines, Database_Stats_ORACLE stats)
        {
            foreach (var raw in lines)
            {
                var line = raw?.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split('|');
                if (parts.Length < 2) continue;

                switch (parts[0].Trim())
                {
                    case "DBSIZE":
                        stats.DataSizeMB = Math.Round(ParseDouble(parts[1]), 2);
                        break;

                    case "SESSIONS":
                        if (int.TryParse(parts[1].Trim(), out var n))
                            stats.ActiveConnections = n;
                        break;

                    case "SGA":
                        stats.SGASizeMB = Math.Round(ParseDouble(parts[1]), 2);
                        break;

                    case "PGA":
                        stats.PGAAllocatedMB = Math.Round(ParseDouble(parts[1]), 2);
                        break;

                    case "ARCHIVELOG":
                        stats.ArchiveLogMode = parts[1].Trim();
                        break;

                    case "INSTANCE":
                        if (parts.Length >= 2) stats.InstanceStatus = parts[1].Trim();
                        if (parts.Length >= 3) stats.InstanceName = parts[2].Trim();
                        if (parts.Length >= 4) stats.HostName = parts[3].Trim();
                        if (parts.Length >= 5) stats.OracleVersion = parts[4].Trim();
                        break;

                    case "TS" when parts.Length >= 6:
                        stats.Tablespaces!.Add(new Tablespace
                        {
                            TableSpaceName = parts[1].Trim(),
                            TotalMB = Math.Round(ParseDouble(parts[2]), 2),
                            UsedMB = Math.Round(ParseDouble(parts[3]), 2),
                            FreeMB = Math.Round(ParseDouble(parts[4]), 2),
                            UsedPercent = Math.Round(ParseDouble(parts[5]), 1)
                        });
                        break;

                    case "SESS" when parts.Length >= 5:
                        stats.Sessions!.Add(new Session
                        {
                            Username = parts[1].Trim(),
                            Maschine = parts[2].Trim(),
                            Program = parts[3].Trim(),
                            Status = parts[4].Trim()
                        });
                        break;
                }
            }
        }

        internal static double ParseDouble(string s)
            => double.TryParse(s.Trim().Replace(',', '.'),
                               NumberStyles.Any,
                               CultureInfo.InvariantCulture,
                               out var v) ? v : 0d;
    }
}