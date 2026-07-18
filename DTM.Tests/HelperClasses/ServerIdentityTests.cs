using FluentAssertions;
using Xunit;

namespace DTM.Tests.HelperClasses;

public class ServerIdentityTests
{
    [Fact]
    public void Equals_TrueForSameTypAndServer()
    {
        var a = new ServerIdentity(DbServer.ServerTyp.MSSQL, "FOC-SQL01");
        var b = new ServerIdentity(DbServer.ServerTyp.MSSQL, "FOC-SQL01");
        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equals_CaseInsensitiveOnServerHostname()
    {
        var a = new ServerIdentity(DbServer.ServerTyp.MSSQL, "FOC-SQL01");
        var b = new ServerIdentity(DbServer.ServerTyp.MSSQL, "foc-sql01");
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equals_FalseForDifferentTyp()
    {
        var a = new ServerIdentity(DbServer.ServerTyp.MSSQL, "host01");
        var b = new ServerIdentity(DbServer.ServerTyp.ORACLE, "host01");
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_FalseForDifferentServer()
    {
        var a = new ServerIdentity(DbServer.ServerTyp.MSSQL, "host01");
        var b = new ServerIdentity(DbServer.ServerTyp.MSSQL, "host02");
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void ToString_HasTypAndServer()
    {
        var id = new ServerIdentity(DbServer.ServerTyp.ORACLE, "olvm.lhp.intern");
        id.ToString().Should().Be("ORACLE:olvm.lhp.intern");
    }

    [Fact]
    public void CanBeUsedAsDictionaryKey_WithCaseInsensitiveLookup()
    {
        Dictionary<ServerIdentity, string> dict = new()
        {
            [new ServerIdentity(DbServer.ServerTyp.MSSQL, "FOC-SQL01")] = "prod",
            [new ServerIdentity(DbServer.ServerTyp.MSSQL, "DEVFOC-SQL01")] = "dev",
        };

        dict[new ServerIdentity(DbServer.ServerTyp.MSSQL, "foc-sql01")].Should().Be("prod");
        dict.Should().HaveCount(2);
    }
}
