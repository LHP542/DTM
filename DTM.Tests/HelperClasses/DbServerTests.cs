using FluentAssertions;
using Xunit;

namespace DTM.Tests.HelperClasses;

public class DbServerTests
{
    [Fact]
    public void Constructor_StoresCredential()
    {
        var cred = new ServerCredential("srv");
        new DbServer(DbServer.ServerTyp.MSSQL, cred).serverCredential.Should().BeSameAs(cred);
    }

    [Fact]
    public void ServerCredential_Property_ReturnsStoredValue()
    {
        var cred = new ServerCredential("sql01", "sa", "pass", "MyDB", "");
        var server = new DbServer(DbServer.ServerTyp.MSSQL, cred);
        server.serverCredential!.Server.Should().Be("sql01");
        server.serverCredential.User.Should().Be("sa");
    }

    [Fact]
    public void Constructor_StoresTyp()
    {
        var server = new DbServer(DbServer.ServerTyp.ORACLE, new ServerCredential("orahost"));
        server.Typ.Should().Be(DbServer.ServerTyp.ORACLE);
    }

    [Fact]
    public void Identity_CombinesTypAndServerHostname()
    {
        var server = new DbServer(DbServer.ServerTyp.MSSQL, new ServerCredential("FOC-SQL01"));
        server.Identity.Should().Be(new ServerIdentity(DbServer.ServerTyp.MSSQL, "FOC-SQL01"));
    }

    [Fact]
    public void ServerTyp_ContainsOracle()
    {
        Enum.GetValues<DbServer.ServerTyp>().Should().Contain(DbServer.ServerTyp.ORACLE);
    }

    [Fact]
    public void ServerTyp_ContainsMssql()
    {
        Enum.GetValues<DbServer.ServerTyp>().Should().Contain(DbServer.ServerTyp.MSSQL);
    }

    [Fact]
    public void ServerTyp_ContainsPostgreSQL()
    {
        Enum.GetValues<DbServer.ServerTyp>().Should().Contain(DbServer.ServerTyp.PostgreSQL);
    }

    [Fact]
    public void ServerTyp_Count_IsThree()
    {
        Enum.GetValues<DbServer.ServerTyp>().Should().HaveCount(3);
    }

    [Fact]
    public void Backend_DefaultsToFocSql()
    {
        var server = new DbServer(DbServer.ServerTyp.MSSQL, new ServerCredential("srv"));
        server.Backend.Should().Be(ServerBackend.FocSql);
    }

    [Fact]
    public void Backend_CanBeSetToOdbcDirect()
    {
        var server = new DbServer(
            DbServer.ServerTyp.MSSQL,
            new ServerCredential("dmz-sql"),
            ServerBackend.OdbcDirect);
        server.Backend.Should().Be(ServerBackend.OdbcDirect);
    }
}
