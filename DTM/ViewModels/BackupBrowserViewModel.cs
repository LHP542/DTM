using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DTM.Data.Mssql;
using DTM.Data.Terminal;
using NLog;

namespace DTM.ViewModels;

/// <summary>
/// ViewModel fuer den Backup-Browser-Dialog. Laedt asynchron alle
/// Backup-Dateien der ausgewaehlten MSSQL-DB.
///
/// Phase 10.4c: Backend-Switch. Bei FocSql-Servern via
/// <see cref="BackupBrowserService"/> (FOC-SQL Get-DbBackups im eigenen
/// PS-Runspace); bei OdbcDirect via <see cref="OdbcMssqlActionService"/>
/// (msdb.dbo.backupset). UI ist identisch — der User sieht keinen
/// Unterschied.
///
/// Oracle wird in v1 nicht unterstuetzt — der Dialog wird fuer Oracle gar
/// nicht erst geoeffnet (Filter in MainWindowViewModel).
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

    /// <summary>
    /// Vom MainWindowViewModel vor dem Anzeigen aufzurufen. Setzt DB,
    /// Server-Host und (Phase 10.4c) optional den OdbcActionService fuer
    /// den OdbcDirect-Pfad.
    /// </summary>
    public async Task LoadAsync(string database, string? server = null,
                                 OdbcMssqlActionService? odbcActions = null)
    {
        DatabaseName = database;
        ServerHost = server;
        OdbcActions = odbcActions;
        IsLoading = true;
        ErrorMessage = null;
        Backups.Clear();
        HasBackups = false;
        SelectedBackup = null;

        try
        {
            IReadOnlyList<MssqlBackup> list = odbcActions is not null
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
    /// Startet den Restore. Bei OdbcDirect: direkt via
    /// <see cref="OdbcMssqlActionService.RestoreBackupAsync"/> mit dem
    /// vollen Path aus msdb. Bei FOC-SQL: Invoke-DbRestore-Aufruf im
    /// pwsh-Tab. Bestaetigung passiert im Code-Behind (ConfirmWindow).
    /// </summary>
    public void PerformRestore(MssqlBackup backup)
    {
        if (backup is null || string.IsNullOrWhiteSpace(DatabaseName)) return;

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
