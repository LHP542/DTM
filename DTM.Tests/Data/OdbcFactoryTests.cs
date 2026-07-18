using FluentAssertions;
using Xunit;

namespace DTM.Tests.Data;

public class OdbcFactoryTests
{
    private static ServerCredential Cred(string server) =>
        new(server, "user", "pass", "db", string.Empty);

    [Fact]
    public void Get_DATA_TwoDifferentMssqlServers_ReturnsDifferentInstances()
    {
        var factory = new OdbcFactory();

        var a = factory.Get_DATA("MSSQL", Cred("FOC-SQL01"));
        var b = factory.Get_DATA("MSSQL", Cred("DEVFOC-SQL01"));

        a.Should().NotBeNull();
        b.Should().NotBeNull();
        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void Get_DATA_SameServerTwice_ReturnsSameInstance()
    {
        var factory = new OdbcFactory();

        var first  = factory.Get_DATA("MSSQL", Cred("FOC-SQL01"));
        var second = factory.Get_DATA("MSSQL", Cred("FOC-SQL01"));

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void Get_DATA_ServerHostCaseInsensitive_ReturnsSameInstance()
    {
        var factory = new OdbcFactory();

        var upper = factory.Get_DATA("MSSQL", Cred("FOC-SQL01"));
        var lower = factory.Get_DATA("MSSQL", Cred("foc-sql01"));

        upper.Should().BeSameAs(lower);
    }

    [Fact]
    public void Get_DATA_TwoDifferentOracleServers_ReturnsDifferentInstances()
    {
        var factory = new OdbcFactory();

        var a = factory.Get_DATA("ORACLE", Cred("olvm-mgm.lhp.intern"));
        var b = factory.Get_DATA("ORACLE", Cred("olvm-mgm.devlhp.intern"));

        a.Should().NotBeNull();
        b.Should().NotBeNull();
        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void Get_DATA_MssqlAndOracleSameHost_ReturnsDifferentInstances()
    {
        var factory = new OdbcFactory();

        var mssql  = factory.Get_DATA("MSSQL",  Cred("host1"));
        var oracle = factory.Get_DATA("ORACLE", Cred("host1"));

        mssql.Should().NotBeSameAs(oracle);
    }

    [Fact]
    public void Get_DATA_UnknownType_ReturnsNull()
    {
        var factory = new OdbcFactory();

        factory.Get_DATA("MARIADB", Cred("host1")).Should().BeNull();
    }
}
