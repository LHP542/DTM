using DTM.ViewModels.TreeNodes;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.ViewModels.TreeNodes;

public class DatabaseNodeViewModelTests
{
    private static DatabaseInfo MakeInfo(string name, DatabaseStatus status = DatabaseStatus.up)
        => new() { Name = name, Status = status, Id = "1", FQDN = "srv.local" };

    [Fact]
    public void Constructor_Header_ContainsName()
    {
        var vm = new DatabaseNodeViewModel(MakeInfo("MyDB"), DbServer.ServerTyp.MSSQL);
        vm.Header.Should().Contain("MyDB");
    }

    [Fact]
    public void Constructor_Header_ContainsStatus()
    {
        var vm = new DatabaseNodeViewModel(MakeInfo("MyDB", DatabaseStatus.down), DbServer.ServerTyp.MSSQL);
        vm.Header.Should().Contain("down");
    }

    [Fact]
    public void Constructor_Header_Format_NameParenStatus()
    {
        var vm = new DatabaseNodeViewModel(MakeInfo("HR", DatabaseStatus.up), DbServer.ServerTyp.ORACLE);
        vm.Header.Should().Be("HR (up)");
    }

    [Fact]
    public void Constructor_Database_IsAccessible()
    {
        var info = MakeInfo("TestDB");
        var vm = new DatabaseNodeViewModel(info, DbServer.ServerTyp.MSSQL);
        vm.Database.Should().BeSameAs(info);
        vm.ServerTyp.Should().Be(DbServer.ServerTyp.MSSQL);
    }
}
