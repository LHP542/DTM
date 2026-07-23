namespace DTM.ViewModels.TreeNodes;

/// <summary>
/// Top-Level-Knoten im DB-Baum — gruppiert alle <see cref="ServerNodeViewModel"/>
/// eines bestimmten <see cref="DB_SERVER.ServerTyp"/>. Statischer Container,
/// kein async-Loading; die Server-Children werden vom MainWindowViewModel beim
/// Aufbau der RootNodes hinzugefuegt.
///
/// Header ist der Typ-Name (z. B. „MSSQL", „ORACLE"). Bei Selektion passiert
/// nichts ausser dem Standard-Expand der Children.
/// </summary>
public sealed class ServerGroupNodeViewModel : NodeViewModelBase
{
    public DB_SERVER.ServerTyp ServerTyp { get; }

    public ServerGroupNodeViewModel(DB_SERVER.ServerTyp typ)
    {
        ServerTyp = typ;
        Header = typ.ToString();
        IsExpanded = true;  // Default: Gruppe auf, Server direkt sichtbar
    }
}
