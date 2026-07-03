using DTM.Data.Mssql;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Data;

/// <summary>
/// Whitelist-Validierung fuer die 10.3b-Actions. Die eigentliche SQL-
/// Ausfuehrung braucht einen echten SQL Server (Integration-Test, laeuft
/// bei Lars auf dem Test-Container). Fuer Unit-Tests exposed der Service
/// die Whitelist als statische Predicates — kein Async-Roundtrip, kein
/// versehentlicher ODBC-Connect-Timeout in der Test-Suite.
/// </summary>
public class OdbcMssqlActionServiceTests
{
    [Theory]
    [InlineData("FULL")]
    [InlineData("SIMPLE")]
    [InlineData("BULK_LOGGED")]
    [InlineData("full")]
    [InlineData("bulk_logged")]
    public void IsValidRecoveryMode_AcceptsCanonicalModes(string mode)
    {
        OdbcMssqlActionService.IsValidRecoveryMode(mode).Should().BeTrue();
    }

    [Theory]
    [InlineData("READ_ONLY")]
    [InlineData("OFF")]
    [InlineData("")]
    [InlineData("FULL;DROP DATABASE X")]
    public void IsValidRecoveryMode_RejectsAnythingElse(string mode)
    {
        OdbcMssqlActionService.IsValidRecoveryMode(mode).Should().BeFalse();
    }

    [Theory]
    [InlineData("CHECKSUM")]
    [InlineData("TORN_PAGE_DETECTION")]
    [InlineData("NONE")]
    [InlineData("checksum")]
    public void IsValidPageVerify_AcceptsCanonicalModes(string pv)
    {
        OdbcMssqlActionService.IsValidPageVerify(pv).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("SOMETHING")]
    [InlineData("NONE; DROP DATABASE X")]
    public void IsValidPageVerify_RejectsAnythingElse(string pv)
    {
        OdbcMssqlActionService.IsValidPageVerify(pv).Should().BeFalse();
    }

    // ---- Phase 10.3c: Snapshot-Naming ----

    [Fact]
    public void BuildSnapshotName_UsesDbAndTimestamp()
    {
        var ts = new DateTime(2026, 7, 3, 15, 22, 47);
        OdbcMssqlActionService.BuildSnapshotName("MyDb", ts)
            .Should().Be("MyDb_Snapshot_20260703152247");
    }

    [Fact]
    public void BuildSnapshotName_IsCollisionFree_ForDistinctTimestamps()
    {
        var t1 = new DateTime(2026, 7, 3, 15, 22, 47);
        var t2 = t1.AddSeconds(1);
        OdbcMssqlActionService.BuildSnapshotName("db", t1)
            .Should().NotBe(OdbcMssqlActionService.BuildSnapshotName("db", t2));
    }

    [Fact]
    public void BuildSnapshotName_PreservesCaseOfDatabase()
    {
        OdbcMssqlActionService.BuildSnapshotName("MyMixedCaseDb", DateTime.Now)
            .Should().StartWith("MyMixedCaseDb_Snapshot_");
    }

    // ---- Phase 10.3d: Backup-Path-Naming ----

    [Fact]
    public void BuildBackupPath_UsesFlatLayout_WithDbSubdirAndTimestamp()
    {
        var ts = new DateTime(2026, 7, 3, 15, 22, 0);
        var path = OdbcMssqlActionService.BuildBackupPath(@"C:\Backups", "MyDb", ts);

        // Flach: <root>\<db>\<db>-<yyyyMMdd_HHmm>.bak.
        // Kein "01 Taeglich"-Unterordner mehr (unterschiedlich zum
        // FOC-SQL-Layout).
        path.Should().EndWith("MyDb-20260703_1522.bak");
        path.Should().Contain("MyDb");
    }

    [Fact]
    public void BuildBackupPath_TrimsTrailingSeparators_FromRoot()
    {
        var ts = new DateTime(2026, 7, 3, 15, 22, 0);
        var withSlash = OdbcMssqlActionService.BuildBackupPath(@"C:\Backups\", "db", ts);
        var withoutSlash = OdbcMssqlActionService.BuildBackupPath(@"C:\Backups", "db", ts);
        withSlash.Should().Be(withoutSlash);
    }

    [Fact]
    public void BuildBackupPath_HasDbSubdirectory()
    {
        var ts = new DateTime(2026, 7, 3, 15, 22, 0);
        var path = OdbcMssqlActionService.BuildBackupPath(@"C:\Backups", "MyDb", ts);

        // <root>\<db>\<file> — die zweite Ebene ist immer der DB-Name.
        var parts = path.Replace('/', '\\').Split('\\');
        parts.Should().Contain("MyDb");
    }
}
