using System.Collections;
using System.Management.Automation;
using DTM.Data.Terminal;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Terminal;

public class DtmCredMapBuilderTests
{
    private static DB_SERVER MssqlWithRemote(string host, string user, string pw) =>
        new(DB_SERVER.ServerTyp.MSSQL,
            new ServerCredential(host, "sa", "sqlpass", "Master", "", user, pw));

    private static DB_SERVER MssqlNoRemote(string host) =>
        new(DB_SERVER.ServerTyp.MSSQL, new ServerCredential(host, "sa", "sqlpass"));

    private static DB_SERVER OracleWithRemote(string host) =>
        new(DB_SERVER.ServerTyp.ORACLE,
            new ServerCredential(host, "system", "orapass", "ORCL", "", "IGNORED", "IGNORED"));

    [Fact]
    public void Build_EmptyList_ReturnsEmptyHashtable()
    {
        var map = DtmCredMapBuilder.Build(new List<DB_SERVER>());
        map.Should().NotBeNull();
        map.Count.Should().Be(0);
    }

    [Fact]
    public void Build_MssqlWithoutRemote_NotIncluded()
    {
        var map = DtmCredMapBuilder.Build(new[] { MssqlNoRemote("FOC-SQL01") });
        map.Count.Should().Be(0);
    }

    [Fact]
    public void Build_Oracle_AlwaysIgnored()
    {
        // Oracle geht ueber SSH-Keys, kein WinRM/PSCredential — auch mit
        // gesetztem RemoteUser darf Oracle NIE in der Map landen.
        var map = DtmCredMapBuilder.Build(new[] { OracleWithRemote("oravm.dmz") });
        map.Count.Should().Be(0);
    }

    [Fact]
    public void Build_MssqlWithRemote_PopulatesPsCredential()
    {
        var servers = new[]
        {
            MssqlWithRemote("dmz-sql01", "DMZ\\svc-dtm", "DmzP@ss!")
        };

        var map = DtmCredMapBuilder.Build(servers);
        map.Count.Should().Be(1);
        map.ContainsKey("dmz-sql01").Should().BeTrue();

        var cred = map["dmz-sql01"] as PSCredential;
        cred.Should().NotBeNull();
        cred!.UserName.Should().Be("DMZ\\svc-dtm");
        // SecureString darf nicht als Klartext gelesen werden — nur Laenge prueft die Integritaet.
        cred.Password.Length.Should().Be("DmzP@ss!".Length);
    }

    [Fact]
    public void Build_KeysAreCaseInsensitive()
    {
        var servers = new[] { MssqlWithRemote("DMZ-SQL01", "DMZ\\svc", "p") };
        var map = DtmCredMapBuilder.Build(servers);

        map.ContainsKey("dmz-sql01").Should().BeTrue();
        map.ContainsKey("DMZ-SQL01").Should().BeTrue();
    }

    [Fact]
    public void Build_MixedList_OnlyMssqlWithRemoteIncluded()
    {
        var servers = new List<DB_SERVER>
        {
            MssqlWithRemote("dmz-sql01", "DMZ\\svc", "p1"),
            MssqlNoRemote("FOC-SQL01"),
            OracleWithRemote("oravm.dmz"),
            MssqlWithRemote("dmz-sql02", "DMZ\\svc2", "p2")
        };

        var map = DtmCredMapBuilder.Build(servers);
        map.Count.Should().Be(2);
        map.ContainsKey("dmz-sql01").Should().BeTrue();
        map.ContainsKey("dmz-sql02").Should().BeTrue();
        map.ContainsKey("FOC-SQL01").Should().BeFalse();
        map.ContainsKey("oravm.dmz").Should().BeFalse();
    }

    [Fact]
    public void Build_NullServerList_ReturnsEmpty()
    {
        var map = DtmCredMapBuilder.Build(null!);
        map.Should().NotBeNull();
        map.Count.Should().Be(0);
    }
}
