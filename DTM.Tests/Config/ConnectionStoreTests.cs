using DTM.Config;
using FluentAssertions;
using Xunit;
using SystemFile = System.IO.File;

namespace DTM.Tests.Config;

[Collection("serial")]
public class ConnectionStoreTests : IDisposable
{
    private readonly string _tmp = Path.Combine(
        Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
    private readonly string _original;

    public ConnectionStoreTests()
    {
        _original = ConnectionStore._path;
        ConnectionStore._path = _tmp;
    }

    public void Dispose()
    {
        // .broken/.tmp mit aufraeumen — die Quarantaene-Tests lassen sonst
        // Dateien im Temp-Verzeichnis liegen.
        foreach (string p in new[] { _tmp, _tmp + ".broken", _tmp + ".tmp" })
        {
            if (SystemFile.Exists(p)) SystemFile.Delete(p);
        }
        ConnectionStore._path = _original;
    }

    // --- Protect / Unprotect ---

    [Fact]
    public void Protect_Empty_ReturnsEmpty()
    {
        ConnectionStore.Protect(string.Empty).Should().BeEmpty();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("Pass!@#")]
    [InlineData("")]
    public void Protect_Unprotect_RoundTrip(string plain)
    {
        string protected_ = ConnectionStore.Protect(plain);
        ConnectionStore.Unprotect(protected_).Should().Be(plain);
    }

    [Fact]
    public void Unprotect_InvalidBase64_ReturnsEmpty()
    {
        ConnectionStore.Unprotect("!!!not_base64!!!").Should().BeEmpty();
    }

    // --- Load / Save ---

    [Fact]
    public void Load_NoFile_ReturnsEmpty()
    {
        ConnectionStore.Load().Should().BeEmpty();
    }

    [Fact]
    public void Save_Then_Load_RoundTrip()
    {
        var entries = new List<ConnectionEntry>
        {
            new() { Key = "MSSQL", Server = "srv1", User = "sa" }
        };

        ConnectionStore.Save(entries);
        var loaded = ConnectionStore.Load();

        loaded.Should().HaveCount(1);
        loaded[0].Key.Should().Be("MSSQL");
        loaded[0].Server.Should().Be("srv1");
    }

    [Fact]
    public void Save_Then_Load_PreservesAllFields()
    {
        var entry = new ConnectionEntry
        {
            Key = "ORACLE",
            Server = "orasrv",
            User = "system",
            Database = "ORCL",
            ConnectionString = "DSN=test"
        };
        entry.PlainPassword = "secret";

        ConnectionStore.Save([entry]);
        var loaded = ConnectionStore.Load();

        loaded.Should().HaveCount(1);
        var e = loaded[0];
        e.Key.Should().Be("ORACLE");
        e.Server.Should().Be("orasrv");
        e.User.Should().Be("system");
        e.Database.Should().Be("ORCL");
        e.ConnectionString.Should().Be("DSN=test");
        e.PlainPassword.Should().Be("secret");
    }

    [Fact]
    public void Load_CorruptJson_ReturnsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_tmp)!);
        SystemFile.WriteAllText(_tmp, "{ not valid json [[[");
        ConnectionStore.Load().Should().BeEmpty();
    }

    [Fact]
    public void Load_CorruptJson_QuarantinesFileAsBroken()
    {
        // Kernschutz gegen Totalverlust: die kaputte Datei darf nicht einfach
        // liegen bleiben und beim naechsten Save ueberschrieben werden — sonst
        // sind alle Server samt DPAPI-Passwoertern unwiederbringlich weg.
        Directory.CreateDirectory(Path.GetDirectoryName(_tmp)!);
        SystemFile.WriteAllText(_tmp, "{ not valid json [[[");

        ConnectionStore.Load().Should().BeEmpty();

        SystemFile.Exists(_tmp).Should().BeFalse("die defekte Datei wurde weggeraeumt");
        SystemFile.Exists(_tmp + ".broken").Should().BeTrue();
        SystemFile.ReadAllText(_tmp + ".broken").Should().Be("{ not valid json [[[");
    }

    [Fact]
    public void Save_LeavesNoTempFileBehind()
    {
        ConnectionStore.Save([new ConnectionEntry { Key = "MSSQL", Server = "srv1" }]);

        SystemFile.Exists(_tmp).Should().BeTrue();
        SystemFile.Exists(_tmp + ".tmp").Should().BeFalse("das .tmp wird beim Move verbraucht");
    }

    [Fact]
    public void Save_OverwritesExistingFileCompletely()
    {
        // File.Move(overwrite: true) darf keine Reste der laengeren Vorgaenger-
        // Datei stehen lassen (waere bei einem In-Place-Write moeglich).
        ConnectionStore.Save([
            new ConnectionEntry { Key = "MSSQL", Server = "srv1" },
            new ConnectionEntry { Key = "ORACLE", Server = "srv2" }
        ]);
        ConnectionStore.Save([new ConnectionEntry { Key = "MSSQL", Server = "srv1" }]);

        var loaded = ConnectionStore.Load();
        loaded.Should().HaveCount(1);
        SystemFile.ReadAllText(_tmp).Should().NotContain("srv2");
    }

    [Fact]
    public void Load_LegacyJson_WithoutRemoteFields_DeserializesWithDefaults()
    {
        // Bestandssetups (vor Phase 9) haben keine RemoteUser/RemotePasswordProtected-
        // Felder. System.Text.Json muss fehlende Properties still auf leer setzen —
        // sonst reisst der Update die connections.json bei bestehenden Nutzern auf.
        Directory.CreateDirectory(Path.GetDirectoryName(_tmp)!);
        string legacyJson = """
        [
          {
            "Key": "MSSQL",
            "Server": "FOC-SQL01",
            "User": "sa",
            "PasswordProtected": "",
            "Database": "Master",
            "ConnectionString": ""
          }
        ]
        """;
        SystemFile.WriteAllText(_tmp, legacyJson);

        var loaded = ConnectionStore.Load();
        loaded.Should().HaveCount(1);
        loaded[0].RemoteUser.Should().BeEmpty();
        loaded[0].RemotePasswordProtected.Should().BeEmpty();
        loaded[0].PlainRemotePassword.Should().BeEmpty();
    }

    [Fact]
    public void Save_Then_Load_PreservesRemoteCredentialFields()
    {
        var entry = new ConnectionEntry
        {
            Key = "MSSQL",
            Server = "dmz-sql01",
            User = "sa",
            Database = "Master",
            RemoteUser = "DMZ\\svc-dtm"
        };
        entry.PlainPassword = "sqlpass";
        entry.PlainRemotePassword = "DmzP@ss!";

        ConnectionStore.Save([entry]);
        var loaded = ConnectionStore.Load();

        loaded.Should().HaveCount(1);
        var e = loaded[0];
        e.RemoteUser.Should().Be("DMZ\\svc-dtm");
        e.RemotePasswordProtected.Should().NotBeEmpty();
        e.RemotePasswordProtected.Should().NotBe("DmzP@ss!");
        e.PlainRemotePassword.Should().Be("DmzP@ss!");
    }

    [Fact]
    public void Load_LegacyJson_WithoutBackend_DefaultsToFocSql()
    {
        // Legacy connections.json vor Phase 10 hat kein Backend-Feld.
        // System.Text.Json muss auf Default (FocSql) fallen — sonst blockt
        // der Bestandsuser den Update.
        Directory.CreateDirectory(Path.GetDirectoryName(_tmp)!);
        string legacyJson = """
        [
          { "Key": "MSSQL", "Server": "FOC-SQL01", "User": "sa",
            "PasswordProtected": "", "Database": "Master", "ConnectionString": "" }
        ]
        """;
        SystemFile.WriteAllText(_tmp, legacyJson);

        var loaded = ConnectionStore.Load();
        loaded.Should().HaveCount(1);
        loaded[0].Backend.Should().Be(ServerBackend.FocSql);
    }

    [Fact]
    public void Save_Then_Load_PreservesBackend()
    {
        var entry = new ConnectionEntry
        {
            Key = "MSSQL",
            Server = "dmz-sql01",
            User = "sa",
            Database = "Master",
            Backend = ServerBackend.OdbcDirect
        };

        ConnectionStore.Save([entry]);
        var loaded = ConnectionStore.Load();

        loaded.Should().HaveCount(1);
        loaded[0].Backend.Should().Be(ServerBackend.OdbcDirect);
    }

    [Fact]
    public void Save_Backend_IsWrittenAsString()
    {
        // Lesbarkeit von connections.json ist ein Feature; wenn irgendwer die
        // Datei manuell inspiziert, soll "OdbcDirect" dort stehen, nicht 1.
        var entry = new ConnectionEntry
        {
            Key = "MSSQL",
            Server = "dmz-sql01",
            Backend = ServerBackend.OdbcDirect
        };

        ConnectionStore.Save([entry]);
        string raw = SystemFile.ReadAllText(_tmp);
        raw.Should().Contain("\"OdbcDirect\"");
    }
}
