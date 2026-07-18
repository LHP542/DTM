using DTM.ORACLE;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Data;

public class OracleOdbcTests
{
    // ------------------------------------------------------------------ ParseDouble

    [Theory]
    [InlineData("123.45", 123.45)]
    [InlineData("123,45", 123.45)]
    [InlineData("", 0.0)]
    [InlineData("abc", 0.0)]
    [InlineData("-5.5", -5.5)]
    [InlineData("42", 42.0)]
    [InlineData("0", 0.0)]
    public void ParseDouble_VariousInputs(string input, double expected)
    {
        OracleOdbcClient.ParseDouble(input).Should().BeApproximately(expected, 0.0001);
    }

    [Fact]
    public void ParseDouble_Whitespace_Trimmed()
    {
        OracleOdbcClient.ParseDouble(" 99.9 ").Should().BeApproximately(99.9, 0.0001);
    }

    // ------------------------------------------------------------------ ParseOracleStats

    private static OracleDatabaseStats NewStats() => new()
    {
        Sessions = new List<Session>(),
        Tablespaces = new List<Tablespace>()
    };

    [Fact]
    public void ParseOracleStats_DbSize_Parsed()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats(["DBSIZE|1024.5"], stats);
        stats.DataSizeMB.Should().BeApproximately(1024.5, 0.01);
    }

    [Fact]
    public void ParseOracleStats_DbSize_CommaDecimal()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats(["DBSIZE|1024,5"], stats);
        stats.DataSizeMB.Should().BeApproximately(1024.5, 0.01);
    }

    [Fact]
    public void ParseOracleStats_Sessions_Parsed()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats(["SESSIONS|7"], stats);
        stats.ActiveConnections.Should().Be(7);
    }

    [Fact]
    public void ParseOracleStats_Sessions_Invalid_DoesNotCrash()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats(["SESSIONS|notanumber"], stats);
        stats.ActiveConnections.Should().Be(0);
    }

    [Fact]
    public void ParseOracleStats_Sga_Parsed()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats(["SGA|512.3"], stats);
        stats.SGASizeMB.Should().BeApproximately(512.3, 0.01);
    }

    [Fact]
    public void ParseOracleStats_Pga_Parsed()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats(["PGA|64.0"], stats);
        stats.PGAAllocatedMB.Should().BeApproximately(64.0, 0.01);
    }

    [Fact]
    public void ParseOracleStats_Archivelog_Parsed()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats(["ARCHIVELOG|ARCHIVELOG"], stats);
        stats.ArchiveLogMode.Should().Be("ARCHIVELOG");
    }

    [Fact]
    public void ParseOracleStats_Instance_AllFields()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats(["INSTANCE|OPEN|ORCL|myhost|19.3.0.0"], stats);
        stats.InstanceStatus.Should().Be("OPEN");
        stats.InstanceName.Should().Be("ORCL");
        stats.HostName.Should().Be("myhost");
        stats.OracleVersion.Should().Be("19.3.0.0");
    }

    [Fact]
    public void ParseOracleStats_Instance_PartialFields_OnlyStatusSet()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats(["INSTANCE|OPEN"], stats);
        stats.InstanceStatus.Should().Be("OPEN");
        stats.InstanceName.Should().BeNull();
    }

    [Fact]
    public void ParseOracleStats_Tablespace_Added()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats(["TS|SYSTEM|1000|800|200|80.0"], stats);
        stats.Tablespaces.Should().HaveCount(1);
        stats.Tablespaces![0].TableSpaceName.Should().Be("SYSTEM");
        stats.Tablespaces[0].TotalMB.Should().BeApproximately(1000, 0.01);
        stats.Tablespaces[0].UsedMB.Should().BeApproximately(800, 0.01);
        stats.Tablespaces[0].FreeMB.Should().BeApproximately(200, 0.01);
        stats.Tablespaces[0].UsedPercent.Should().BeApproximately(80.0, 0.01);
    }

    [Fact]
    public void ParseOracleStats_Session_Added()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats(["SESS|SYS|hostname|sqlplus|ACTIVE"], stats);
        stats.Sessions.Should().HaveCount(1);
        stats.Sessions![0].Username.Should().Be("SYS");
        stats.Sessions[0].Maschine.Should().Be("hostname");
        stats.Sessions[0].Program.Should().Be("sqlplus");
        stats.Sessions[0].Status.Should().Be("ACTIVE");
    }

    [Fact]
    public void ParseOracleStats_EmptyLines_Skipped()
    {
        var stats = NewStats();
        Action act = () => OracleOdbcClient.ParseOracleStats(["", "  ", "DBSIZE|100"], stats);
        act.Should().NotThrow();
        stats.DataSizeMB.Should().BeApproximately(100, 0.01);
    }

    [Fact]
    public void ParseOracleStats_UnknownPrefix_Skipped()
    {
        var stats = NewStats();
        Action act = () => OracleOdbcClient.ParseOracleStats(["UNKNOWN|foo|bar"], stats);
        act.Should().NotThrow();
    }

    [Fact]
    public void ParseOracleStats_TsInsufficientFields_NotAdded()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats(["TS|incomplete"], stats);
        stats.Tablespaces.Should().BeEmpty();
    }

    [Fact]
    public void ParseOracleStats_SessInsufficientFields_NotAdded()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats(["SESS|only|two"], stats);
        stats.Sessions.Should().BeEmpty();
    }

    [Fact]
    public void ParseOracleStats_MultipleEntries()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats([
            "TS|SYSTEM|1000|800|200|80.0",
            "TS|SYSAUX|500|400|100|80.0",
            "TS|USERS|200|100|100|50.0",
            "SESS|SYS|host1|prog|ACTIVE",
            "SESS|APP|host2|app|INACTIVE"
        ], stats);
        stats.Tablespaces.Should().HaveCount(3);
        stats.Sessions.Should().HaveCount(2);
    }

    [Fact]
    public void ParseOracleStats_Rounding_DbSize()
    {
        var stats = NewStats();
        OracleOdbcClient.ParseOracleStats(["DBSIZE|1234.5678"], stats);
        // Math.Round(..., 2) → 1234.57
        stats.DataSizeMB.Should().BeApproximately(1234.57, 0.001);
    }
}
