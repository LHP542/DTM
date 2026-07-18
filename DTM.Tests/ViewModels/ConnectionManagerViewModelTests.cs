using DTM.Data.Config;
using DTM.Data.Terminal;
using DTM.ViewModels;
using FluentAssertions;
using Xunit;
using SystemFile = System.IO.File;

namespace DTM.Tests.ViewModels;

[Collection("serial")]
public class ConnectionManagerViewModelTests : IDisposable
{
    private readonly string _connTmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
    private readonly string _settTmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
    private readonly string _origConn;
    private readonly string _origSett;
    private readonly FocSqlConfig _origFoc;

    public ConnectionManagerViewModelTests()
    {
        _origConn = ConnectionStore._path;
        _origSett = AppSettingsStore._path;
        _origFoc = FocSqlRuntime.Current;
        ConnectionStore._path = _connTmp;
        AppSettingsStore._path = _settTmp;
    }

    public void Dispose()
    {
        if (SystemFile.Exists(_connTmp)) SystemFile.Delete(_connTmp);
        if (SystemFile.Exists(_settTmp)) SystemFile.Delete(_settTmp);
        ConnectionStore._path = _origConn;
        AppSettingsStore._path = _origSett;
        FocSqlRuntime.Current = _origFoc;
    }

    private static ConnectionEntry MakeEntry(string key = "MSSQL", string server = "srv1")
        => new() { Key = key, Server = server, User = "sa", Database = "Master" };

    // ------------------------------------------------------------------ Constructor

    [Fact]
    public void Constructor_EmptyStore_NoConnections()
    {
        new ConnectionManagerViewModel().Connections.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_LoadsExistingConnections()
    {
        ConnectionStore.Save([MakeEntry("MSSQL", "sql01"), MakeEntry("ORACLE", "ora01")]);
        var vm = new ConnectionManagerViewModel();
        vm.Connections.Should().HaveCount(2);
    }

    [Fact]
    public void Constructor_LoadsFocSqlValues()
    {
        AppSettingsStore.SaveFocSql(new FocSqlConfig { SambaSource = "\\\\srv\\share", ModulePath = "C:\\mod" });
        var vm = new ConnectionManagerViewModel();
        vm.SambaSource.Should().Be("\\\\srv\\share");
        vm.ModulePath.Should().Be("C:\\mod");
    }

    // ------------------------------------------------------------------ AddEntry

    [Fact]
    public void AddEntry_AddsToConnections()
    {
        var vm = new ConnectionManagerViewModel();
        vm.AddEntry(MakeEntry());
        vm.Connections.Should().HaveCount(1);
    }

    [Fact]
    public void AddEntry_SetsSelectedConnection()
    {
        var vm = new ConnectionManagerViewModel();
        var entry = MakeEntry();
        vm.AddEntry(entry);
        vm.SelectedConnection.Should().BeSameAs(vm.Connections[0]);
    }

    [Fact]
    public void AddEntry_SavesEntryToDisk()
    {
        var vm = new ConnectionManagerViewModel();
        vm.AddEntry(MakeEntry("MSSQL", "newserver"));
        var loaded = ConnectionStore.Load();
        loaded.Should().HaveCount(1);
        loaded[0].Server.Should().Be("newserver");
    }

    // ------------------------------------------------------------------ UpdateEntry

    [Fact]
    public void UpdateEntry_WithSelection_ReplacesEntry()
    {
        var vm = new ConnectionManagerViewModel();
        var original = MakeEntry("MSSQL", "old");
        vm.AddEntry(original);
        vm.SelectedConnection = vm.Connections[0];

        var updated = MakeEntry("MSSQL", "new");
        vm.UpdateEntry(updated);

        vm.Connections.Should().HaveCount(1);
        vm.Connections[0].Server.Should().Be("new");
    }

    [Fact]
    public void UpdateEntry_NoSelection_DoesNotCrash()
    {
        var vm = new ConnectionManagerViewModel();
        vm.AddEntry(MakeEntry());
        vm.SelectedConnection = null;
        Action act = () => vm.UpdateEntry(MakeEntry("ORACLE", "ora"));
        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------ DeleteSelected

    [Fact]
    public void DeleteSelected_RemovesEntry()
    {
        var vm = new ConnectionManagerViewModel();
        vm.AddEntry(MakeEntry());
        vm.SelectedConnection = vm.Connections[0];
        vm.DeleteSelected();
        vm.Connections.Should().BeEmpty();
    }

    [Fact]
    public void DeleteSelected_ClearsSelectedConnection()
    {
        var vm = new ConnectionManagerViewModel();
        vm.AddEntry(MakeEntry());
        vm.SelectedConnection = vm.Connections[0];
        vm.DeleteSelected();
        vm.SelectedConnection.Should().BeNull();
    }

    [Fact]
    public void DeleteSelected_NullSelection_IsNoOp()
    {
        var vm = new ConnectionManagerViewModel();
        vm.AddEntry(MakeEntry());
        vm.SelectedConnection = null;
        Action act = () => vm.DeleteSelected();
        act.Should().NotThrow();
        vm.Connections.Should().HaveCount(1);
    }

    // ------------------------------------------------------------------ SaveFocSql

    [Fact]
    public void SaveFocSql_UpdatesFocSqlRuntimeCurrent()
    {
        var vm = new ConnectionManagerViewModel
        {
            SambaSource = "\\\\newsrv\\share",
            ModulePath = ""
        };
        vm.SaveFocSql();
        FocSqlRuntime.Current.SambaSource.Should().Be("\\\\newsrv\\share");
    }

    [Fact]
    public void SaveFocSql_PersistsToDisk()
    {
        var vm = new ConnectionManagerViewModel
        {
            SambaSource = "\\\\srv\\share",
            ModulePath = "C:\\mod"
        };
        vm.SaveFocSql();
        var loaded = AppSettingsStore.LoadFocSql();
        loaded.SambaSource.Should().Be("\\\\srv\\share");
    }

    [Fact]
    public void SaveFocSql_BothFieldsPersisted()
    {
        var vm = new ConnectionManagerViewModel
        {
            SambaSource = "samba",
            ModulePath = "modpath"
        };
        vm.SaveFocSql();
        var loaded = AppSettingsStore.LoadFocSql();
        loaded.SambaSource.Should().Be("samba");
        loaded.ModulePath.Should().Be("modpath");
    }

    [Fact]
    public void AddThenReload_RoundTrip()
    {
        var vm1 = new ConnectionManagerViewModel();
        vm1.AddEntry(MakeEntry("ORACLE", "orasrv"));

        var vm2 = new ConnectionManagerViewModel();
        vm2.Connections.Should().HaveCount(1);
        vm2.Connections[0].Key.Should().Be("ORACLE");
        vm2.Connections[0].Server.Should().Be("orasrv");
    }
}
