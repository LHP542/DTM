using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DTM.Data.Mssql;
using NLog;

namespace DTM.ViewModels;

/// <summary>
/// Auswahl-Dialog fuer MSSQL-DB-Snapshots (Phase 10.4d, OdbcDirect-Modus).
/// Ersetzt den interaktiven pwsh-Tab-Weg (Read-Host) im FOC-SQL-Modus fuer
/// alle DMZ-Server. Bei FocSql-Servern kommt weiterhin der pwsh-Tab-Weg
/// zum Einsatz.
///
/// Vereinigt Restore und Drop: nach der Snapshot-Auswahl entscheidet der
/// Klick auf einen der beiden Buttons im Window die Aktion. Der Aufrufer
/// (MainWindowViewModel) prueft <see cref="MssqlSnapshotSelectResult.Action"/>
/// und ruft den entsprechenden <see cref="OdbcMssqlActionService"/>-Aufruf
/// auf.
/// </summary>
public sealed partial class MssqlSnapshotSelectViewModel : ViewModelBase
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty] private string _databaseName = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasSnapshots;
    [ObservableProperty] private MssqlSnapshotInfo? _selectedSnapshot;
    [ObservableProperty] private MssqlSnapshotAction _initialAction = MssqlSnapshotAction.Restore;

    public ObservableCollection<MssqlSnapshotInfo> Snapshots { get; } = new();

    public async Task LoadAsync(string database, OdbcMssqlActionService svc,
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
            var list = await svc.ListSnapshotsAsync(database, ct).ConfigureAwait(true);
            foreach (var s in list) Snapshots.Add(s);
            HasSnapshots = Snapshots.Count > 0;
            SelectedSnapshot = Snapshots.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "ListSnapshots fuer '{0}' fehlgeschlagen.", database);
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}

/// <summary>Was der User im Dialog ausgewaehlt hat.</summary>
public enum MssqlSnapshotAction
{
    Restore,
    Drop
}

/// <summary>Rueckgabe des Dialogs.</summary>
public sealed record MssqlSnapshotSelectResult(
    MssqlSnapshotAction Action,
    MssqlSnapshotInfo Snapshot);
