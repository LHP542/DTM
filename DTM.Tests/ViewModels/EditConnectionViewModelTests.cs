using DTM.Data.Config;
using DTM.ViewModels;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.ViewModels;

public class EditConnectionViewModelTests
{
    [Fact]
    public void ToEntry_MapsAllFieldsCorrectly()
    {
        var vm = new EditConnectionViewModel
        {
            SelectedServerType = DbServer.ServerTyp.MSSQL,
            Server = "sql01",
            User = "sa",
            Password = "secret",
            Database = "MyDB",
            ConnectionString = "DSN=x"
        };

        var entry = vm.ToEntry();
        entry.Key.Should().Be("MSSQL");
        entry.Server.Should().Be("sql01");
        entry.User.Should().Be("sa");
        entry.PlainPassword.Should().Be("secret");
        entry.Database.Should().Be("MyDB");
        entry.ConnectionString.Should().Be("DSN=x");
    }

    [Fact]
    public void ToEntry_Password_IsEncrypted_InEntry()
    {
        var vm = new EditConnectionViewModel { Password = "plaintext" };
        var entry = vm.ToEntry();
        entry.PasswordProtected.Should().NotBe("plaintext");
        entry.PasswordProtected.Should().NotBeEmpty();
    }

    [Fact]
    public void FromEntry_RoundTrip_PreservesFields()
    {
        var original = new ConnectionEntry
        {
            Key = "ORACLE",
            Server = "orasrv",
            User = "system",
            Database = "ORCL",
            ConnectionString = "DSN=ora"
        };
        original.PlainPassword = "orapass";

        var vm = new EditConnectionViewModel(original);
        var rebuilt = vm.ToEntry();

        rebuilt.Key.Should().Be("ORACLE");
        rebuilt.Server.Should().Be("orasrv");
        rebuilt.User.Should().Be("system");
        rebuilt.PlainPassword.Should().Be("orapass");
        rebuilt.Database.Should().Be("ORCL");
        rebuilt.ConnectionString.Should().Be("DSN=ora");
    }

    [Fact]
    public void ToEntry_EmptyConnectionString_Preserved()
    {
        var vm = new EditConnectionViewModel { ConnectionString = "" };
        vm.ToEntry().ConnectionString.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_UnknownKey_DefaultsToMssql()
    {
        var entry = new ConnectionEntry { Key = "UNKNOWN", Server = "", User = "", Database = "" };
        var vm = new EditConnectionViewModel(entry);
        vm.SelectedServerType.Should().Be(DbServer.ServerTyp.MSSQL);
    }

    [Fact]
    public void ServerTypes_ContainsAllEnumValues()
    {
        EditConnectionViewModel.ServerTypes.Should().BeEquivalentTo(
            Enum.GetValues<DbServer.ServerTyp>());
    }

    [Fact]
    public void IsMssql_TogglesWithSelectedServerType()
    {
        var vm = new EditConnectionViewModel { SelectedServerType = DbServer.ServerTyp.MSSQL };
        vm.IsMssql.Should().BeTrue();

        vm.SelectedServerType = DbServer.ServerTyp.ORACLE;
        vm.IsMssql.Should().BeFalse();
    }

    [Fact]
    public void ToEntry_Mssql_PropagatesRemoteCredentials()
    {
        var vm = new EditConnectionViewModel
        {
            SelectedServerType = DbServer.ServerTyp.MSSQL,
            Server = "dmz-sql01",
            RemoteUser = "DMZ\\svc-dtm",
            RemotePassword = "DmzP@ss!"
        };

        var entry = vm.ToEntry();
        entry.RemoteUser.Should().Be("DMZ\\svc-dtm");
        entry.PlainRemotePassword.Should().Be("DmzP@ss!");
        entry.RemotePasswordProtected.Should().NotBe("DmzP@ss!");
    }

    [Fact]
    public void ToEntry_Oracle_DropsRemoteCredentials()
    {
        // Auch wenn im VM die Remote-Felder gefuellt sind (z. B. Typ-Wechsel
        // von MSSQL nach Oracle): Oracle nutzt SSH-Keys, DPAPI-Blob soll
        // nicht "vergessen" persistiert bleiben.
        var vm = new EditConnectionViewModel
        {
            SelectedServerType = DbServer.ServerTyp.ORACLE,
            RemoteUser = "should-be-dropped",
            RemotePassword = "should-be-dropped"
        };

        var entry = vm.ToEntry();
        entry.RemoteUser.Should().BeEmpty();
        entry.RemotePasswordProtected.Should().BeEmpty();
        entry.PlainRemotePassword.Should().BeEmpty();
    }

    [Fact]
    public void FromEntry_RoundTrip_PreservesRemoteFields()
    {
        var original = new ConnectionEntry
        {
            Key = "MSSQL",
            Server = "dmz-sql01",
            User = "sa",
            Database = "Master",
            RemoteUser = "DMZ\\svc-dtm"
        };
        original.PlainPassword = "sqlpass";
        original.PlainRemotePassword = "DmzP@ss!";

        var vm = new EditConnectionViewModel(original);
        var rebuilt = vm.ToEntry();

        rebuilt.RemoteUser.Should().Be("DMZ\\svc-dtm");
        rebuilt.PlainRemotePassword.Should().Be("DmzP@ss!");
    }

    [Fact]
    public void SelectedBackend_DefaultsToFocSql()
    {
        new EditConnectionViewModel().SelectedBackend.Should().Be(ServerBackend.FocSql);
    }

    [Fact]
    public void ToEntry_Mssql_PropagatesBackend()
    {
        var vm = new EditConnectionViewModel
        {
            SelectedServerType = DbServer.ServerTyp.MSSQL,
            SelectedBackend = ServerBackend.OdbcDirect
        };
        vm.ToEntry().Backend.Should().Be(ServerBackend.OdbcDirect);
    }

    [Fact]
    public void ToEntry_Oracle_ForcesBackendToFocSql()
    {
        // Selbst wenn der User zuvor OdbcDirect gewaehlt hat und dann
        // auf Oracle umschaltet: fuer Oracle gibt es keinen ODBC-Direct-
        // Weg. Persistiere immer FocSql, damit der DMZ-Weg nicht
        // versehentlich fuer Oracle aktiv wird.
        var vm = new EditConnectionViewModel
        {
            SelectedServerType = DbServer.ServerTyp.ORACLE,
            SelectedBackend = ServerBackend.OdbcDirect
        };
        vm.ToEntry().Backend.Should().Be(ServerBackend.FocSql);
    }

    [Fact]
    public void FromEntry_RoundTrip_PreservesBackend()
    {
        var original = new ConnectionEntry
        {
            Key = "MSSQL",
            Server = "dmz-sql01",
            Backend = ServerBackend.OdbcDirect
        };

        var vm = new EditConnectionViewModel(original);
        vm.SelectedBackend.Should().Be(ServerBackend.OdbcDirect);
        vm.ToEntry().Backend.Should().Be(ServerBackend.OdbcDirect);
    }

    [Fact]
    public void Backends_ContainsAllEnumValues()
    {
        EditConnectionViewModel.Backends.Should().BeEquivalentTo(
            Enum.GetValues<ServerBackend>());
    }
}
