using DTM.Config;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Config;

public class ConnectionEntryTests
{
    [Theory]
    [InlineData("Secret1")]
    [InlineData("")]
    [InlineData("P@ssw0rd")]
    public void PlainPassword_SetAndGet_RoundTrip(string plain)
    {
        var entry = new ConnectionEntry();
        entry.PlainPassword = plain;
        entry.PlainPassword.Should().Be(plain);
    }

    [Fact]
    public void PlainPassword_StoresProtected_NotPlaintext()
    {
        var entry = new ConnectionEntry();
        entry.PlainPassword = "hunter2";
        entry.PasswordProtected.Should().NotBe("hunter2");
        entry.PasswordProtected.Should().NotBeEmpty();
    }

    [Fact]
    public void ToCredential_MapsAllFields()
    {
        var entry = new ConnectionEntry
        {
            Key = "MSSQL",
            Server = "sqlsrv",
            User = "sa",
            Database = "MyDB",
            ConnectionString = "DSN=test"
        };
        entry.PlainPassword = "pass1";

        var cred = entry.ToCredential();
        cred.Server.Should().Be("sqlsrv");
        cred.User.Should().Be("sa");
        cred.Password.Should().Be("pass1");
        cred.Datenbank.Should().Be("MyDB");
        cred.ConnectionString.Should().Be("DSN=test");
    }

    [Fact]
    public void ToCredential_EmptyConnectionString_Preserved()
    {
        var entry = new ConnectionEntry { Server = "s", User = "u", Database = "d", ConnectionString = "" };
        entry.PlainPassword = "";
        var cred = entry.ToCredential();
        cred.ConnectionString.Should().BeEmpty();
    }

    [Fact]
    public void PlainRemotePassword_RoundTrip_AndPropagatesToCredential()
    {
        var entry = new ConnectionEntry
        {
            Server = "dmz-sql01",
            RemoteUser = "DMZ\\svc-dtm"
        };
        entry.PlainRemotePassword = "DmzP@ss!";

        entry.RemotePasswordProtected.Should().NotBe("DmzP@ss!");
        entry.RemotePasswordProtected.Should().NotBeEmpty();
        entry.PlainRemotePassword.Should().Be("DmzP@ss!");

        var cred = entry.ToCredential();
        cred.RemoteUser.Should().Be("DMZ\\svc-dtm");
        cred.RemotePassword.Should().Be("DmzP@ss!");
        cred.HasRemoteCredential.Should().BeTrue();
    }

    [Fact]
    public void HasRemoteCredential_DefaultsToFalse_WhenFieldsEmpty()
    {
        var cred = new ConnectionEntry { Server = "s", User = "u" }.ToCredential();
        cred.RemoteUser.Should().BeEmpty();
        cred.RemotePassword.Should().BeEmpty();
        cred.HasRemoteCredential.Should().BeFalse();
    }
}
