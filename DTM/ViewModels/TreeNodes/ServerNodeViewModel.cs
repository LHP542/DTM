using Avalonia.Threading;
using NLog;

namespace DTM.ViewModels.TreeNodes;

/// <summary>
/// Tree-Knoten fuer einen einzelnen Server (Hostname). Liegt unter einem
/// <see cref="ServerGroupNodeViewModel"/>. Beim Expand laedt der Knoten
/// die Datenbank-Liste seines Servers via <see cref="IDTM_DATA"/> und legt
/// pro DB einen <see cref="DatabaseNodeViewModel"/> als Child an.
/// </summary>
public sealed class ServerNodeViewModel : NodeViewModelBase
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private readonly IDTM_DATA _data;
    private bool _childrenLoaded;

    public ServerIdentity Identity { get; }
    public DB_SERVER.ServerTyp ServerTyp => Identity.Typ;
    public string ServerHost => Identity.Server;

    public ServerNodeViewModel(ServerIdentity identity, IDTM_DATA data)
    {
        Identity = identity;
        _data = data;
        Header = string.IsNullOrWhiteSpace(identity.Server) ? identity.Typ.ToString() : identity.Server;
    }

    protected override void OnExpanded()
    {
        _ = EnsureChildrenLoadedAsync();
    }

    public async Task EnsureChildrenLoadedAsync()
    {
        if (_childrenLoaded) return;
        _childrenLoaded = true;
        await LoadChildrenAsync();
    }

    private async Task LoadChildrenAsync()
    {
        IsLoading = true;
        try
        {
            var dbs = await Task.Run(() => _data.get_Database_Names(Identity));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Children.Clear();
                foreach (var db in dbs)
                {
                    Children.Add(new DatabaseNodeViewModel(db, Identity));
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Fehler beim Laden der DBs für {Identity}.");
            _childrenLoaded = false;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
