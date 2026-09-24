using DTM;
using DTM.MariaDb;
using FluentAssertions;
using MySqlConnector;
using Xunit;

namespace DTM.Tests.Data;

/// <summary>
/// Alles, was sich ohne laufenden MariaDB-Server prüfen lässt: der Aufbau
/// des ConnectionStrings. Die Abfragen selbst brauchen einen Server und sind
/// damit kein Unit-Test-Stoff.
/// </summary>
public class MariaDbConnectorTests
{
    private static MySqlConnectionStringBuilder Build(ServerCredential c)
    {
        using MariaDb_Connector con = new(c);
        return new MySqlConnectionStringBuilder(con.BuildConnectionString());
    }

    [Fact]
    public void ConnectionString_UsesDefaultPort()
    {
        var b = Build(new ServerCredential("db01", "root", "geheim"));

        b.Server.Should().Be("db01");
        b.Port.Should().Be(3306);
        b.UserID.Should().Be("root");
    }

    [Theory]
    [InlineData("db01:3307", "db01", 3307u)]
    [InlineData("10.1.2.3:13306", "10.1.2.3", 13306u)]
    public void ConnectionString_ReadsPortFromServerField(string input, string host, uint port)
    {
        // Sonst müsste man für einen abweichenden Port den kompletten
        // ConnectionString von Hand schreiben.
        var b = Build(new ServerCredential(input, "root", "geheim"));

        b.Server.Should().Be(host);
        b.Port.Should().Be(port);
    }

    [Theory]
    [InlineData("db01:0")]
    [InlineData("db01:99999")]
    [InlineData("db01:keinPort")]
    public void ConnectionString_InvalidPort_FallsBackToDefault(string input)
    {
        // Ein unsinniger Port darf nicht dazu führen, dass der Hostname
        // abgeschnitten wird — dann liefe die Verbindung gegen den falschen
        // Rechner statt klar zu scheitern.
        var b = Build(new ServerCredential(input, "root", "geheim"));

        b.Server.Should().Be(input);
        b.Port.Should().Be(3306);
    }

    [Theory]
    [InlineData("fe80::1")]
    [InlineData("2001:db8::8a2e:370:7334")]
    public void ConnectionString_IPv6WithoutPort_StaysIntact(string address)
    {
        // Ohne Sonderbehandlung würde hier alles hinter dem letzten ":" als
        // Port gelesen — aus "fe80::1" würde Host "fe80:" auf Port 1, und die
        // Verbindung liefe gegen den falschen Rechner statt klar zu scheitern.
        var b = Build(new ServerCredential(address, "root", "geheim"));

        b.Server.Should().Be(address);
        b.Port.Should().Be(3306);
    }

    [Fact]
    public void ConnectionString_IPv6WithPort_UsesBracketForm()
    {
        var b = Build(new ServerCredential("[fe80::1]:3307", "root", "geheim"));

        b.Server.Should().Be("fe80::1");
        b.Port.Should().Be(3307);
    }

    [Fact]
    public void ConnectionString_ExplicitOneWins()
    {
        var c = new ServerCredential("ignoriert", "ignoriert", "ignoriert",
            ConnectionString: "Server=eigener;Port=3310;User ID=u;Password=p;");

        var b = Build(c);

        b.Server.Should().Be("eigener");
        b.Port.Should().Be(3310);
    }

    [Fact]
    public void ConnectionString_SetsNoDefaultSchema()
    {
        // DTM wechselt das Schema über die Datenbankliste und qualifiziert
        // jede Abfrage selbst. Ein fest gesetztes Schema würde die Verbindung
        // scheitern lassen, sobald es auf dem Server fehlt.
        var b = Build(new ServerCredential("db01", "root", "geheim", Datenbank: "Master"));

        b.Database.Should().BeEmpty();
    }
}

