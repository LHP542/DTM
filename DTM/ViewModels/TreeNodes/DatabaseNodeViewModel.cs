namespace DTM.ViewModels.TreeNodes;

public sealed class DatabaseNodeViewModel : NodeViewModelBase
{
    public Database_Info Database { get; }

    /// <summary>
    /// Identität des Servers, auf dem diese DB liegt — enthält Typ + Hostname.
    /// Wird vom MainWindowViewModel für FOC-SQL-Aufrufe verwendet, damit die
    /// richtige MSSQL-Instanz / der richtige Oracle-Host angesteuert wird.
    /// </summary>
    public ServerIdentity ServerIdentity { get; }

    /// <summary>Bequeme Kurzform — vorher Standalone-Property auf diesem VM.</summary>
    public DB_SERVER.ServerTyp ServerTyp => ServerIdentity.Typ;

    public DatabaseNodeViewModel(Database_Info info, ServerIdentity serverIdentity)
    {
        Database = info;
        ServerIdentity = serverIdentity;
        Header = $"{info.Name} ({info.Status})";
    }

    /// <summary>
    /// Convenience-Konstruktor für Tests: legt eine synthetische Identität
    /// mit leerem Server-Hostname an. Produktion nutzt immer die Variante mit
    /// echtem <see cref="ServerIdentity"/>.
    /// </summary>
    public DatabaseNodeViewModel(Database_Info info, DB_SERVER.ServerTyp typ)
        : this(info, new ServerIdentity(typ, string.Empty)) { }
}
