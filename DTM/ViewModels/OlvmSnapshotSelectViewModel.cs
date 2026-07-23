using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DTM.Data.Olvm;
using NLog;

namespace DTM.ViewModels;

/// <summary>
/// Auswahl-Dialog fuer OLVM-VM-Snapshots (Phase 11.3/11.6). Analog zu
/// <see cref="MssqlSnapshotSelectViewModel"/>. Listing kommt aus dem
/// oVirt-REST-API (<see cref="OlvmSnapshotService.ListAsync"/>).
///
/// Restore und Delete sind aktuell disabled — die Ansible-Playbooks
/// dafuer stehen noch nicht (Phase 11.4/11.5). Der Dialog dient bis
/// dahin als reine Anzeige.
/// </summary>
public sealed partial class OlvmSnapshotSelectViewModel : ViewModelBase
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty] private string _databaseName = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasSnapshots;
    [ObservableProperty] private OlvmSnapshotInfo? _selectedSnapshot;
    [ObservableProperty] private MssqlSnapshotAction _initialAction = MssqlSnapshotAction.Restore;

    // Restore + Delete sind Phase 11.4/11.5 — bis die Ansible-Playbooks
    // stehen bleiben beide Buttons im Window disabled. Der Property-Wert
    // ist hier fest false; wird gesetzt sobald die Playbooks bereit sind.
    public bool RestoreEnabled => false;
    public bool DeleteEnabled => false;

    public ObservableCollection<OlvmSnapshotInfo> Snapshots { get; } = new();

    public async Task LoadAsync(string database, string vmId, OlvmSnapshotService svc,
                                MssqlSnapshotAction initialAction,
                                CancellationToken ct = default)
    {
        DatabaseName = database;
        InitialAction = initialAction;
        IsLoading = true;
        ErrorMessage = null;
        Snapshots.Clear();
        HasSnapshots = false;
        SelectedSnapshot = null;

        try
        {
            var list = await svc.ListAsync(vmId, ct).ConfigureAwait(true);
            foreach (var s in list) Snapshots.Add(s);
            HasSnapshots = Snapshots.Count > 0;
            SelectedSnapshot = Snapshots.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "OLVM ListSnapshots fuer '{0}' fehlgeschlagen.", database);
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}

/// <summary>Rueckgabe des OLVM-Snapshot-Dialogs.</summary>
public sealed record OlvmSnapshotSelectResult(
    MssqlSnapshotAction Action,
    OlvmSnapshotInfo Snapshot);
