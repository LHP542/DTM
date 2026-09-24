using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DTM.Data.MariaDb;
using DTM.Data.Mssql;
using DTM.Data.Terminal;
using NLog;

namespace DTM.ViewModels;

/// <summary>
/// ViewModel fuer den Backup-Browser-Dialog. Laedt asynchron alle
/// Sicherungen der ausgewaehlten Datenbank.
///
/// Drei Quellen, eine Ansicht:
/// <list type="bullet">
///   <item>MSSQL/FocSql via <see cref="BackupBrowserService"/> (FOC-SQL
///   <c>Get-DbBackups</c> im eigenen PS-Runspace),</item>
///   <item>MSSQL/OdbcDirect via <see cref="OdbcMssqlActionService"/>
///   (<c>msdb.dbo.backupset</c>),</item>
///   <item>MariaDB via <see cref="MariaDbBackupService"/> (Dumps im
///   konfigurierten Backup-Verzeichnis).</item>
/// </list>
/// Der User sieht in allen drei Faellen dieselbe Liste.
///
/// Oracle wird nicht unterstuetzt — der Dialog wird dafuer gar nicht erst
/// geoeffnet (Filter in MainWindowViewModel).
/// </summary>
public sealed partial class BackupBrowserViewModel : ViewModelBase
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private readonly BackupBrowserService _service;

    [ObservableProperty] private string _databaseName = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasBackups;
    [ObservableProperty] private MssqlBackup? _selectedBackup;

    public ObservableCollection<MssqlBackup> Backups { get; } = new();

    public BackupBrowserViewModel(BackupBrowserService service)
    {
        _service = service;
    }

    /// <summary>Server-Hostname fuer den FOC-SQL-Restore-Aufruf.</summary>
    public string? ServerHost { get; set; }

    /// <summary>Wenn gesetzt: OdbcDirect-Pfad; sonst FOC-SQL-Pfad.</summary>
    public OdbcMssqlActionService? OdbcActions { get; set; }

    /// <summary>Wenn gesetzt: MariaDB-Pfad; schlaegt die beiden anderen.</summary>
    public MariaDbBackupService? MariaDbBackups { get; set; }

    /// <summary>
    /// Zusatz im Bestaetigungs-Dialog vor dem Restore. Bei MSSQL beendet das
    /// Modul die Sessions selbst; der MariaDB-Client tut das nicht — dort
    /// waere der Satz schlicht falsch.
    /// </summary>
    public string RestoreNote => MariaDbBackups is not null
        ? "Offene Verbindungen werden dabei nicht beendet — laufende Schreibzugriffe "
          + "koennen den eingespielten Stand sofort wieder veraendern."
        : "Alle aktiven Sessions werden vorher beendet.";

    /// <summary>
    /// Vom MainWindowViewModel vor dem Anzeigen aufzurufen. Setzt DB,
    /// Server-Host und — je nach Server — den Dienst, ueber den geladen und
    /// zurueckgespielt wird.
    /// </summary>
    public async Task LoadAsync(string database, string? server = null,
                                 OdbcMssqlActionService? odbcActions = null,
                                 MariaDbBackupService? mariaDbBackups = null)
    {
        DatabaseName = database;
        ServerHost = server;
        OdbcActions = odbcActions;
        MariaDbBackups = mariaDbBackups;
        IsLoading = true;
        ErrorMessage = null;
        Backups.Clear();
        HasBackups = false;
        SelectedBackup = null;

        try
        {
            IReadOnlyList<MssqlBackup> list = mariaDbBackups is not null
                ? LoadViaMariaDb(database, mariaDbBackups)
                : odbcActions is not null
                    ? await LoadViaOdbcAsync(database, odbcActions).ConfigureAwait(true)
                    : await _service.FetchAsync(database, server).ConfigureAwait(true);

            foreach (MssqlBackup b in list) Backups.Add(b);
            HasBackups = Backups.Count > 0;
            SelectedBackup = Backups.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Backup-Liste fuer '{0}' fehlgeschlagen.", database);
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static async Task<IReadOnlyList<MssqlBackup>> LoadViaOdbcAsync(
        string database, OdbcMssqlActionService svc)
    {
        var raw = await svc.ListBackupsAsync(database).ConfigureAwait(false);
        // msdb liefert den vollen physical_device_name — das Filename-only
        // Feld baut die UI selbst. LastWriteTime = backup_finish_date.
        return raw
            .Select(b => new MssqlBackup(
                Name: System.IO.Path.GetFileName(b.Path),
                LastWriteTime: b.FinishedAt,
                SizeBytes: b.SizeBytes,
                Path: b.Path))
            .ToList();
    }

    /// <summary>
    /// Dumps aus dem Backup-Verzeichnis. Der Zugriff ist rein lesend auf dem
    /// Dateisystem und schnell genug fuer den UI-Thread — ein
    /// <c>Task.Run</c> waere hier nur Zeremonie.
    /// </summary>
    private static IReadOnlyList<MssqlBackup> LoadViaMariaDb(
        string database, MariaDbBackupService svc)
    {
        return svc.ListBackups(database)
            .Select(b => new MssqlBackup(
                Name: b.FileName,
                LastWriteTime: b.Created,
                SizeBytes: b.SizeBytes,
                Path: b.Path))
            .ToList();
    }

    /// <summary>
    /// Startet den Restore. Bei MariaDB ueber den Client, bei OdbcDirect
    /// direkt via <see cref="OdbcMssqlActionService.RestoreBackupAsync"/> mit
    /// dem vollen Path aus msdb, bei FOC-SQL als Invoke-DbRestore-Aufruf im
    /// pwsh-Tab. Bestaetigung passiert im Code-Behind (ConfirmWindow).
    /// </summary>
    public void PerformRestore(MssqlBackup backup)
    {
        if (backup is null || string.IsNullOrWhiteSpace(DatabaseName)) return;

        if (MariaDbBackups is { } maria)
        {
            _ = RunMariaDbRestoreAsync(maria, backup);
            return;
        }

        if (OdbcActions is { } svc)
        {
            _ = RunOdbcRestoreAsync(svc, backup);
            return;
        }

        // FOC-SQL: Invoke-DbRestore erwartet nur den Filename (Modul baut
        // den Pfad ueber $global:BackupRoot).
        string dbEsc = DatabaseName.Replace("'", "''");
        string fileEsc = backup.Name.Replace("'", "''");
        string script = $"Invoke-DbRestore -Database '{dbEsc}' -BackupFile '{fileEsc}'";
        if (!string.IsNullOrWhiteSpace(ServerHost))
        {
            string srvEsc = ServerHost.Replace("'", "''");
            script += $" -Server '{srvEsc}'";
        }
        TerminalBus.SendScript(script);
    }

    private async Task RunMariaDbRestoreAsync(MariaDbBackupService svc, MssqlBackup backup)
    {
        string label = $"Restore aus '{backup.Name}'";
        TerminalBus.InjectNotice($"[{label} für {DatabaseName} (MariaDB)]");
        try
        {
            Action<string> onInfo = t => TerminalBus.InjectNotice($"  {t}");
            await svc.RestoreAsync(DatabaseName, backup.Path, onInfo).ConfigureAwait(false);
            TerminalBus.InjectNotice($"[{label} fertig für {DatabaseName}]");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "MariaDB-Restore von '{0}' fehlgeschlagen.", backup.Name);
            TerminalBus.InjectNotice($"[FEHLER: {ex.Message}]");
        }
    }

    private async Task RunOdbcRestoreAsync(OdbcMssqlActionService svc, MssqlBackup backup)
    {
        string label = $"Restore aus '{backup.Name}'";
        TerminalBus.InjectNotice($"[{label} für {DatabaseName} (OdbcDirect)]");
        try
        {
            Action<string> onInfo = t => TerminalBus.InjectNotice($"  {t}");
            await svc.RestoreBackupAsync(DatabaseName, backup.Path, onInfo).ConfigureAwait(false);
            TerminalBus.InjectNotice($"[{label} fertig für {DatabaseName}]");
        }
        catch (Exception ex)
        {
            TerminalBus.InjectNotice($"[FEHLER: {ex.Message}]");
        }
    }
}
