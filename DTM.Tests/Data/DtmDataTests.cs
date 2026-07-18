using DTM.Data.Odbc;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Data;

public class DtmDataTests
{
    private sealed class FakeOdbc : IDtmOdbc
    {
        public List<DatabaseInfo> Names { get; set; } = [];
        public DatabaseStats Stats { get; set; } = new MssqlDatabaseStats();

        public List<DatabaseInfo> get_Datenbank_Names() => Names;
        public DatabaseStats GetDatabase_Stats(DatabaseInfo db) => Stats;
    }

    private sealed class FakeFactory : IOdbcFactory
    {
        public string? LastRequested;
        public readonly FakeOdbc Odbc = new();

        public IDtmOdbc? Get_DATA(string name, ServerCredential cred)
        {
            LastRequested = name;
            return name is "MSSQL" or "ORACLE" ? Odbc : null;
        }
    }

    private static (DtmData data, FakeFactory factory, ServerIdentity identity) Make(
        DbServer.ServerTyp typ, string host = "testhost")
    {
        var factory = new FakeFactory();
        var server = new DbServer(typ, new ServerCredential(host, "user", "pass", "db", ""));
        var data = new DtmData(new List<DbServer> { server }, factory);
        return (data, factory, server.Identity);
    }

    [Fact]
    public void GetDatabaseNames_Mssql_RequestsFactoryWithMssql()
    {
        var (data, factory, id) = Make(DbServer.ServerTyp.MSSQL);
        data.get_Database_Names(id);
        factory.LastRequested.Should().Be("MSSQL");
    }

    [Fact]
    public void GetDatabaseNames_Oracle_RequestsFactoryWithOracle()
    {
        var (data, factory, id) = Make(DbServer.ServerTyp.ORACLE);
        data.get_Database_Names(id);
        factory.LastRequested.Should().Be("ORACLE");
    }

    [Fact]
    public void GetDatabaseNames_ReturnsOdbcResult()
    {
        var (data, factory, id) = Make(DbServer.ServerTyp.MSSQL);
        factory.Odbc.Names = [new DatabaseInfo { Name = "TestDB", Id = "1", FQDN = "", Status = DatabaseStatus.up }];
        var result = data.get_Database_Names(id);
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("TestDB");
    }

    [Fact]
    public void GetDatabaseStats_Mssql_RequestsFactoryWithMssql()
    {
        var (data, factory, id) = Make(DbServer.ServerTyp.MSSQL);
        data.get_Database_Stats(id, new DatabaseInfo { Name = "db", Id = "1", FQDN = "", Status = DatabaseStatus.up });
        factory.LastRequested.Should().Be("MSSQL");
    }

    [Fact]
    public void GetDatabaseStats_ReturnsOdbcResult()
    {
        var (data, factory, id) = Make(DbServer.ServerTyp.MSSQL);
        var expected = new MssqlDatabaseStats { Name = "MyDB" };
        factory.Odbc.Stats = expected;
        var result = data.get_Database_Stats(id, new DatabaseInfo { Name = "MyDB", Id = "1", FQDN = "", Status = DatabaseStatus.up });
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public void Servers_ExposedAsList()
    {
        var (data, _, id) = Make(DbServer.ServerTyp.MSSQL);
        data.Servers.Should().HaveCount(1);
        data.Servers[0].Identity.Should().Be(id);
    }

    [Fact]
    public void GetDatabaseNames_UnknownIdentity_ThrowsKeyNotFound()
    {
        var (data, _, _) = Make(DbServer.ServerTyp.MSSQL);
        // Anderer Hostname → nicht in der Liste → ResolveServer wirft KeyNotFoundException
        var unknown = new ServerIdentity(DbServer.ServerTyp.MSSQL, "other-host");
        Action act = () => data.get_Database_Names(unknown);
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Constructor_MultipleServersSameType_AllAccessible()
    {
        var factory = new FakeFactory();
        var s1 = new DbServer(DbServer.ServerTyp.MSSQL, new ServerCredential("hostA"));
        var s2 = new DbServer(DbServer.ServerTyp.MSSQL, new ServerCredential("hostB"));
        var data = new DtmData(new List<DbServer> { s1, s2 }, factory);

        data.Servers.Should().HaveCount(2);
        // Beide ueber ihre Identity einzeln auflosbar.
        Action act1 = () => data.get_Database_Names(s1.Identity);
        Action act2 = () => data.get_Database_Names(s2.Identity);
        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }
}
