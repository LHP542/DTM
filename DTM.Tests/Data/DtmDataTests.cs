using FluentAssertions;
using Xunit;

namespace DTM.Tests.Data;

public class DtmDataTests
{
    private sealed class FakeOdbc : ODBC.IDTM_ODBC
    {
        public List<Database_Info> Names { get; set; } = [];
        public Database_Stats Stats { get; set; } = new Database_Stats_MSSQL();

        public List<Database_Info> get_Datenbank_Names() => Names;
        public Database_Stats GetDatabase_Stats(Database_Info db) => Stats;
    }

    private sealed class FakeFactory : IODBC_Factory
    {
        public string? LastRequested;
        public readonly FakeOdbc Odbc = new();

        public ODBC.IDTM_ODBC? Get_DATA(string name, ServerCredential cred)
        {
            LastRequested = name;
            return name is "MSSQL" or "ORACLE" ? Odbc : null;
        }
    }

    private static (DTM_DATA data, FakeFactory factory, ServerIdentity identity) Make(
        DB_SERVER.ServerTyp typ, string host = "testhost")
    {
        var factory = new FakeFactory();
        var server = new DB_SERVER(typ, new ServerCredential(host, "user", "pass", "db", ""));
        var data = new DTM_DATA(new List<DB_SERVER> { server }, factory);
        return (data, factory, server.Identity);
    }

    [Fact]
    public void GetDatabaseNames_Mssql_RequestsFactoryWithMssql()
    {
        var (data, factory, id) = Make(DB_SERVER.ServerTyp.MSSQL);
        data.get_Database_Names(id);
        factory.LastRequested.Should().Be("MSSQL");
    }

    [Fact]
    public void GetDatabaseNames_Oracle_RequestsFactoryWithOracle()
    {
        var (data, factory, id) = Make(DB_SERVER.ServerTyp.ORACLE);
        data.get_Database_Names(id);
        factory.LastRequested.Should().Be("ORACLE");
    }

    [Fact]
    public void GetDatabaseNames_ReturnsOdbcResult()
    {
        var (data, factory, id) = Make(DB_SERVER.ServerTyp.MSSQL);
        factory.Odbc.Names = [new Database_Info { Name = "TestDB", Id = "1", FQDN = "", Status = Database_Status.up }];
        var result = data.get_Database_Names(id);
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("TestDB");
    }

    [Fact]
    public void GetDatabaseStats_Mssql_RequestsFactoryWithMssql()
    {
        var (data, factory, id) = Make(DB_SERVER.ServerTyp.MSSQL);
        data.get_Database_Stats(id, new Database_Info { Name = "db", Id = "1", FQDN = "", Status = Database_Status.up });
        factory.LastRequested.Should().Be("MSSQL");
    }

    [Fact]
    public void GetDatabaseStats_ReturnsOdbcResult()
    {
        var (data, factory, id) = Make(DB_SERVER.ServerTyp.MSSQL);
        var expected = new Database_Stats_MSSQL { Name = "MyDB" };
        factory.Odbc.Stats = expected;
        var result = data.get_Database_Stats(id, new Database_Info { Name = "MyDB", Id = "1", FQDN = "", Status = Database_Status.up });
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public void Servers_ExposedAsList()
    {
        var (data, _, id) = Make(DB_SERVER.ServerTyp.MSSQL);
        data.Servers.Should().HaveCount(1);
        data.Servers[0].Identity.Should().Be(id);
    }

    [Fact]
    public void GetDatabaseNames_UnknownIdentity_ThrowsKeyNotFound()
    {
        var (data, _, _) = Make(DB_SERVER.ServerTyp.MSSQL);
        // Anderer Hostname → nicht in der Liste → ResolveServer wirft KeyNotFoundException
        var unknown = new ServerIdentity(DB_SERVER.ServerTyp.MSSQL, "other-host");
        Action act = () => data.get_Database_Names(unknown);
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Constructor_MultipleServersSameType_AllAccessible()
    {
        var factory = new FakeFactory();
        var s1 = new DB_SERVER(DB_SERVER.ServerTyp.MSSQL, new ServerCredential("hostA"));
        var s2 = new DB_SERVER(DB_SERVER.ServerTyp.MSSQL, new ServerCredential("hostB"));
        var data = new DTM_DATA(new List<DB_SERVER> { s1, s2 }, factory);

        data.Servers.Should().HaveCount(2);
        // Beide über ihre Identity einzeln auflosbar.
        Action act1 = () => data.get_Database_Names(s1.Identity);
        Action act2 = () => data.get_Database_Names(s2.Identity);
        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }
}
