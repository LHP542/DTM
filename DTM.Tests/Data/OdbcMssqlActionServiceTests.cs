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
}
