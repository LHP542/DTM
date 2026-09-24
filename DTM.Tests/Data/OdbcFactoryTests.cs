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
        var factory = new ODBC_Factory();

        var a = factory.Get_DATA("MSSQL", Cred("FOC-SQL01"));
        var b = factory.Get_DATA("MSSQL", Cred("DEVFOC-SQL01"));

        a.Should().NotBeNull();
        b.Should().NotBeNull();
        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void Get_DATA_SameServerTwice_ReturnsSameInstance()
    {
        var factory = new ODBC_Factory();

        var first  = factory.Get_DATA("MSSQL", Cred("FOC-SQL01"));
        var second = factory.Get_DATA("MSSQL", Cred("FOC-SQL01"));

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void Get_DATA_ServerHostCaseInsensitive_ReturnsSameInstance()
    {
        var factory = new ODBC_Factory();

        var upper = factory.Get_DATA("MSSQL", Cred("FOC-SQL01"));
        var lower = factory.Get_DATA("MSSQL", Cred("foc-sql01"));

        upper.Should().BeSameAs(lower);
    }

    [Fact]
    public void Get_DATA_TwoDifferentOracleServers_ReturnsDifferentInstances()
    {
        var factory = new ODBC_Factory();

        var a = factory.Get_DATA("ORACLE", Cred("olvm-mgm.lhp.intern"));
        var b = factory.Get_DATA("ORACLE", Cred("olvm-mgm.devlhp.intern"));

        a.Should().NotBeNull();
        b.Should().NotBeNull();
        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void Get_DATA_MssqlAndOracleSameHost_ReturnsDifferentInstances()
    {
        var factory = new ODBC_Factory();

        var mssql  = factory.Get_DATA("MSSQL",  Cred("host1"));
        var oracle = factory.Get_DATA("ORACLE", Cred("host1"));

        mssql.Should().NotBeSameAs(oracle);
    }

    [Fact]
    public void Get_DATA_UnknownType_ThrowsWithClearMessage()
    {
        // Frueher kam hier null zurueck — die Aufrufer in DTM_DATA
        // dereferenzieren das Ergebnis aber mit "!", also gab es eine
        // NullReferenceException ohne jeden Hinweis auf die Ursache.
        // (Das Beispiel war bis Phase 16 "MARIADB" — inzwischen implementiert.)
        var factory = new ODBC_Factory();

        Action act = () => factory.Get_DATA("Informix", Cred("host1"));

        act.Should().Throw<NotSupportedException>().WithMessage("*Informix*");
    }

    [Fact]
    public void Get_DATA_MariaDb_ReturnsConnector()
    {
        var factory = new ODBC_Factory();

        factory.Get_DATA("MariaDB", Cred("host1"))
            .Should().BeOfType<DTM.MariaDb.MariaDb_Connector>();
    }

    [Fact]
    public void Get_DATA_IsCaseInsensitive()
    {
        // Die Aufrufer reichen ServerTyp.ToString() durch — also "MariaDB",
        // nicht "MARIADB". Ein case-sensitiver Vergleich ginge ins Leere.
        var factory = new ODBC_Factory();

        factory.Get_DATA("mariadb", Cred("host1"))
            .Should().BeOfType<DTM.MariaDb.MariaDb_Connector>();
    }

    [Fact]
    public void Get_DATA_MariaDb_CachesPerServer()
    {
        var factory = new ODBC_Factory();

        var a = factory.Get_DATA("MariaDB", Cred("host1"));
        var b = factory.Get_DATA("MariaDB", Cred("host1"));
        var other = factory.Get_DATA("MariaDB", Cred("host2"));

        a.Should().BeSameAs(b);
        other.Should().NotBeSameAs(a, "pro Server eine eigene Verbindung");
    }
}
