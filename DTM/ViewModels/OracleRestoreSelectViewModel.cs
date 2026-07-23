using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DTM.Data.Terminal;
using NLog;

namespace DTM.ViewModels;

/// <summary>
/// ViewModel fuer den Oracle-Restore-Vorschau-Dialog. Laedt asynchron
/// die Restore-Points und PDB-Liste der CDB ueber den
/// <see cref="OracleRestoreService"/> und exposes sie ans UI.
/// </summary>
public sealed partial class OracleRestoreSelectViewModel : ViewModelBase
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private readonly OracleRestoreService _service;

    [ObservableProperty] private string _databaseName = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasMultiplePdbs;
    [ObservableProperty] private bool _hasRestorePoints;
    [ObservableProperty] private OracleRestorePoint? _selectedRestorePoint;

    public ObservableCollection<OraclePdb> Pdbs { get; } = new();
    public ObservableCollection<OracleRestorePoint> RestorePoints { get; } = new();

    public OracleRestoreSelectViewModel(OracleRestoreService service)
    {
        _service = service;
    }

    /// <summary>
    /// Holt die Restore-Vorschau fuer <paramref name="database"/>. Bei Fehler
    /// wird <see cref="ErrorMessage"/> gesetzt; das UI zeigt den Fehler an.
    /// </summary>
    public async Task LoadAsync(string database)
    {
        DatabaseName = database;
        IsLoading = true;
        ErrorMessage = null;
        Pdbs.Clear();
        RestorePoints.Clear();
        HasMultiplePdbs = false;
        HasRestorePoints = false;
        SelectedRestorePoint = null;

        try
        {
            OracleRestoreInfo info = await _service.FetchAsync(database);

            foreach (OraclePdb p in info.PdbNames) Pdbs.Add(p);
            foreach (OracleRestorePoint rp in info.RestorePoints) RestorePoints.Add(rp);

            HasMultiplePdbs = Pdbs.Count > 1;
            HasRestorePoints = RestorePoints.Count > 0;
            SelectedRestorePoint = RestorePoints.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Oracle-Restore-Vorschau fuer '{0}' fehlgeschlagen.", database);
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
