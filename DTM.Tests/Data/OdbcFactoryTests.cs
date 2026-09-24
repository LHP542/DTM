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
        // Früher kam hier null zurück — die Aufrufer in DTM_DATA
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

    [Fact]
    public void Dispose_ClosesCachedConnections_AndBlocksFurtherUse()
    {
        var factory = new ODBC_Factory();
        factory.Get_DATA("MariaDB", Cred("host1"));
        factory.Get_DATA("MSSQL", Cred("host2"));

        factory.Dispose();

        // Nach dem Schliessen darf niemand mehr eine Verbindung bekommen —
        // sonst baut ein spaeter eintreffender Hintergrund-Task stillschweigend
        // eine neue auf, die dann wieder niemand schliesst.
        Action act = () => factory.Get_DATA("MariaDB", Cred("host1"));
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var factory = new ODBC_Factory();
        factory.Get_DATA("MariaDB", Cred("host1"));

        factory.Dispose();
        Action second = factory.Dispose;

        second.Should().NotThrow();
    }

    /// <summary>
    /// Der Cache wird nicht nur vom UI-Thread benutzt: die Kennzahlen holt
    /// <c>LoadStatsAsync</c> in einem <c>Task.Run</c>, und der verzögerte
    /// Refresh nach einer Aktion tut dasselbe. Zwei Threads, die gleichzeitig
    /// in ein ungeschütztes Dictionary schreiben, können dessen Buckets
    /// zerlegen — im schlimmsten Fall dreht sich der nächste Lookup endlos.
    /// </summary>
    [Fact]
    public void Get_DATA_IsSafeUnderParallelAccess()
    {
        using var factory = new ODBC_Factory();
        var results = new System.Collections.Concurrent.ConcurrentBag<object?>();

        // Jeder Aufruf trifft einen NEUEN Schlüssel, also jedes Mal einen
        // Schreibzugriff auf das Dictionary — genau die Stelle, an der ein
        // ungeschützter Cache seine Buckets zerlegt. Mit wiederverwendeten
        // Schlüsseln laufen fast alle Aufrufe in den Lesepfad, und der Test
        // wird zum Schönwetter-Test.
        Parallel.For(0, 4000, new ParallelOptions { MaxDegreeOfParallelism = 16 },
            i => results.Add(factory.Get_DATA("MariaDB", Cred($"host{i}"))));

        results.Should().HaveCount(4000);
        results.Should().OnlyContain(r => r != null);
        // 4000 verschiedene Server, also 4000 verschiedene Instanzen — weniger
        // hiesse, der Cache hat unter Last Einträge verloren.
        results.Distinct().Should().HaveCount(4000);
    }
}
