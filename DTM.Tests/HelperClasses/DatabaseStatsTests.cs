using FluentAssertions;
using Xunit;

namespace DTM.Tests.HelperClasses;

public class DatabaseStatsTests
{
    [Fact]
    public void Session_Properties_AreReadWritable()
    {
        var s = new Session { Username = "sa", Maschine = "PC1", Program = "SSMS", Status = "Active" };
        s.Username.Should().Be("sa");
        s.Maschine.Should().Be("PC1");
        s.Program.Should().Be("SSMS");
        s.Status.Should().Be("Active");
    }

    [Fact]
    public void Tablespace_Properties_AreReadWritable()
    {
        var ts = new Tablespace { TableSpaceName = "SYSTEM", TotalMB = 1000, UsedMB = 800, FreeMB = 200, UsedPercent = 80.0 };
        ts.TableSpaceName.Should().Be("SYSTEM");
        ts.TotalMB.Should().Be(1000);
        ts.UsedMB.Should().Be(800);
        ts.FreeMB.Should().Be(200);
        ts.UsedPercent.Should().Be(80.0);
    }

    [Fact]
    public void File_Properties_AreReadWritable()
    {
        var f = new DTM.File { FileLogicalName = "data", Type = "ROWS", FileSizeMB = 512, Growth = 10, IsPercentigGrowth = true };
        f.FileLogicalName.Should().Be("data");
        f.Type.Should().Be("ROWS");
        f.FileSizeMB.Should().Be(512);
        f.Growth.Should().Be(10);
        f.IsPercentigGrowth.Should().BeTrue();
    }

    [Fact]
    public void Database_Stats_DefaultValues_AreZeroOrNull()
    {
        var stats = new DatabaseStats();
        stats.Server.Should().BeNull();
        stats.Name.Should().BeNull();
        stats.State.Should().BeNull();
        stats.DataSizeMB.Should().Be(0);
        stats.ActiveConnections.Should().Be(0);
        stats.Sessions.Should().BeNull();
    }

    [Fact]
    public void Database_Stats_CanSetAllProperties()
    {
        var stats = new DatabaseStats
        {
            Server = "sql01", Name = "MyDB", DatabaseTyp = "MSSQL",
            State = "ONLINE", DataSizeMB = 256, ActiveConnections = 3,
            Sessions = [new Session { Username = "sa" }]
        };
        stats.Server.Should().Be("sql01");
        stats.Sessions.Should().HaveCount(1);
    }

    [Fact]
    public void MssqlStats_IsAssignableToBaseType()
    {
        new MssqlDatabaseStats().Should().BeAssignableTo<DatabaseStats>();
    }

    [Fact]
    public void MssqlStats_SpecificProperties_AreSettable()
    {
        var m = new MssqlDatabaseStats
        {
            RecorveryModel = "FULL", CompatibllityLevel = 160,
            Collation = "Latin1_General_CI_AS", IsReadOnly = false,
            LogSizeMB = 64, TotalSizeMB = 320
        };
        m.RecorveryModel.Should().Be("FULL");
        m.CompatibllityLevel.Should().Be(160);
        m.LogSizeMB.Should().Be(64);
    }

    [Fact]
    public void MssqlStats_DefaultCompatibilityLevel_IsZero()
    {
        new MssqlDatabaseStats().CompatibllityLevel.Should().Be(0);
    }

    [Fact]
    public void OracleStats_IsAssignableToBaseType()
    {
        new OracleDatabaseStats().Should().BeAssignableTo<DatabaseStats>();
    }

    [Fact]
    public void OracleStats_SpecificProperties_AreSettable()
    {
        var o = new OracleDatabaseStats
        {
            InstanceName = "ORCL", OracleVersion = "19.3",
            ArchiveLogMode = "ARCHIVELOG", SGASizeMB = 512, PGAAllocatedMB = 128,
            Tablespaces = [new Tablespace { TableSpaceName = "SYSTEM" }]
        };
        o.InstanceName.Should().Be("ORCL");
        o.OracleVersion.Should().Be("19.3");
        o.Tablespaces.Should().HaveCount(1);
    }
}
