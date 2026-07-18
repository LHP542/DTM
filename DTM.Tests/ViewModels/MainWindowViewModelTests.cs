using DTM.ViewModels;
using DTM.ViewModels.TreeNodes;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private sealed class StubData : IDtmData
    {
        public IReadOnlyList<DbServer> Servers { get; init; } = Array.Empty<DbServer>();
        public List<DatabaseInfo> get_Database_Names(ServerIdentity id) => [];
        public DatabaseStats get_Database_Stats(ServerIdentity id, DatabaseInfo db)
            => new MssqlDatabaseStats();
        public DTM.Data.Mssql.OdbcMssqlActionService GetMssqlActions(ServerIdentity id)
            => throw new NotSupportedException("Stub: MainWindowViewModelTests testen den FOC-SQL-Weg.");
        public DTM.Data.Olvm.OlvmSnapshotService GetOlvmSnapshotService(ServerIdentity id)
            => throw new NotSupportedException("Stub: OLVM-REST wird hier nicht aufgerufen.");
    }

    private static MainWindowViewModel MakeVm(params DbServer.ServerTyp[] types)
    {
        var servers = types.Select(t => new DbServer(t, new ServerCredential())).ToList();
        return new MainWindowViewModel(new StubData { Servers = servers }, servers);
    }

    private static DatabaseNodeViewModel MakeDbNode(string name, DbServer.ServerTyp typ,
        string fqdn = "")
    {
        var info = new DatabaseInfo { Name = name, FQDN = fqdn, Id = "1", Status = DatabaseStatus.up };
        return new DatabaseNodeViewModel(info, typ);
    }

    // ------------------------------------------------------------------ Constructor

    [Fact]
    public void Constructor_EmptyServers_RootNodesEmpty()
    {
        MakeVm().RootNodes.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_TwoServers_RootNodesHasTwo()
    {
        MakeVm(DbServer.ServerTyp.MSSQL, DbServer.ServerTyp.ORACLE)
            .RootNodes.Should().HaveCount(2);
    }

    // ------------------------------------------------------------------ ApplyStats MSSQL

    [Fact]
    public void ApplyStats_Mssql_BackupButtonText_IsBackup()
    {
        var vm = MakeVm();
        vm.ApplyStats(new MssqlDatabaseStats());
        vm.BackupButtonText.Should().Be("Backup");
    }

    [Fact]
    public void ApplyStats_Mssql_RecoveryLabel_IsRecovery()
    {
        var vm = MakeVm();
        vm.ApplyStats(new MssqlDatabaseStats());
        vm.RecoveryLabel.Should().Be("Recovery");
    }

    [Fact]
    public void ApplyStats_Mssql_SetsDbName()
    {
        var vm = MakeVm();
        vm.ApplyStats(new MssqlDatabaseStats { Name = "AdventureWorks" });
        vm.DbName.Should().Be("AdventureWorks");
    }

    [Fact]
    public void ApplyStats_Mssql_SetsDbHost()
    {
        var vm = MakeVm();
        vm.ApplyStats(new MssqlDatabaseStats { Server = "sql01" });
        vm.DbHost.Should().Be("sql01");
    }

    [Fact]
    public void ApplyStats_Mssql_SetsDbVersion_AsCompatLevel()
    {
        var vm = MakeVm();
        vm.ApplyStats(new MssqlDatabaseStats { CompatibllityLevel = 160 });
        vm.DbVersion.Should().Be("160");
    }

    [Fact]
    public void ApplyStats_Mssql_SetsDbSize_WithMbSuffix()
    {
        var vm = MakeVm();
        vm.ApplyStats(new MssqlDatabaseStats { DataSizeMB = 512.5 });
        vm.DbSize.Should().Be("512.5 MB");
    }

    [Fact]
    public void ApplyStats_Mssql_NullFields_FallToDash()
    {
        var vm = MakeVm();
        vm.ApplyStats(new MssqlDatabaseStats { Name = null, Server = null, State = null, RecorveryModel = null });
        vm.DbName.Should().Be("—");
        vm.DbHost.Should().Be("—");
        vm.DbStatus.Should().Be("—");
        vm.RecoveryOrArchiveMode.Should().Be("—");
    }

    // ------------------------------------------------------------------ ApplyStats Oracle

    [Fact]
    public void ApplyStats_Oracle_BackupButtonText_IsDump()
    {
        var vm = MakeVm();
        vm.ApplyStats(new OracleDatabaseStats());
        vm.BackupButtonText.Should().Be("Dump");
    }

    [Fact]
    public void ApplyStats_Oracle_RecoveryLabel_IsArchiveLog()
    {
        var vm = MakeVm();
        vm.ApplyStats(new OracleDatabaseStats());
        vm.RecoveryLabel.Should().Be("ArchiveLog");
    }

    [Fact]
    public void ApplyStats_Oracle_SetsAllOracleFields()
    {
        var vm = MakeVm();
        vm.ApplyStats(new OracleDatabaseStats
        {
            InstanceName = "ORCL",
            Server = "orasrv",
            State = "OPEN",
            OracleVersion = "19.3",
            DataSizeMB = 1024,
            ArchiveLogMode = "ARCHIVELOG"
        });
        vm.DbName.Should().Be("ORCL");
        vm.DbHost.Should().Be("orasrv");
        vm.DbStatus.Should().Be("OPEN");
        vm.DbVersion.Should().Be("19.3");
        vm.DbSize.Should().Be("1024 MB");
        vm.RecoveryOrArchiveMode.Should().Be("ARCHIVELOG");
    }

    // ------------------------------------------------------------------ ApplyStats Sessions

    [Fact]
    public void ApplyStats_SessionsNull_CountIsZero()
    {
        var vm = MakeVm();
        vm.ApplyStats(new MssqlDatabaseStats { Sessions = null });
        vm.ActiveSessionsCount.Should().Be("0");
    }

    [Fact]
    public void ApplyStats_Sessions_SetsCountAndLabel()
    {
        var vm = MakeVm();
        vm.ApplyStats(new MssqlDatabaseStats
        {
            Sessions = [new Session(), new Session(), new Session()]
        });
        vm.ActiveSessionsCount.Should().Be("3");
        vm.ActiveSessionsLabel.Should().Be("Aktive Sessions: 3");
    }

    // ------------------------------------------------------------------ ModuleDatabaseId

    [Fact]
    public void ModuleDatabaseId_Mssql_ReturnsName()
    {
        var node = MakeDbNode("MyDB", DbServer.ServerTyp.MSSQL);
        MainWindowViewModel.ModuleDatabaseId(node).Should().Be("MyDB");
    }

    [Fact]
    public void ModuleDatabaseId_Oracle_WithFqdn_ReturnsFqdn()
    {
        var node = MakeDbNode("VM-ORACLE", DbServer.ServerTyp.ORACLE, fqdn: "ora.company.local");
        MainWindowViewModel.ModuleDatabaseId(node).Should().Be("ora.company.local");
    }

    [Fact]
    public void ModuleDatabaseId_Oracle_EmptyFqdn_FallsBackToName()
    {
        var node = MakeDbNode("VM-ORACLE", DbServer.ServerTyp.ORACLE, fqdn: "");
        MainWindowViewModel.ModuleDatabaseId(node).Should().Be("VM-ORACLE");
    }

    [Fact]
    public void ModuleDatabaseId_Oracle_NullFqdn_FallsBackToName()
    {
        var info = new DatabaseInfo { Name = "VM-ORACLE", FQDN = null, Id = "1", Status = DatabaseStatus.up };
        var node = new DatabaseNodeViewModel(info, DbServer.ServerTyp.ORACLE);
        MainWindowViewModel.ModuleDatabaseId(node).Should().Be("VM-ORACLE");
    }
}
