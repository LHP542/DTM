using FluentAssertions;
using Xunit;

namespace DTM.Tests.HelperClasses;

public class ServerCredentialTests
{
    [Fact]
    public void Constructor_DefaultServer_IsFocSql01()
    {
        new ServerCredential().Server.Should().Be("FOC-SQL01");
    }

    [Fact]
    public void Constructor_DefaultDatenbank_IsMaster()
    {
        new ServerCredential().Datenbank.Should().Be("Master");
    }

    [Fact]
    public void Constructor_DefaultOtherFields_AreEmpty()
    {
        var cred = new ServerCredential();
        cred.User.Should().BeEmpty();
        cred.Password.Should().BeEmpty();
        cred.ConnectionString.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithAllValues_SetsAllProperties()
    {
        var cred = new ServerCredential("srv", "usr", "pwd", "db", "DSN=x");
        cred.Server.Should().Be("srv");
        cred.User.Should().Be("usr");
        cred.Password.Should().Be("pwd");
        cred.Datenbank.Should().Be("db");
        cred.ConnectionString.Should().Be("DSN=x");
    }

    [Fact]
    public void Properties_CanBeModified_AfterConstruction()
    {
        var cred = new ServerCredential();
        cred.Server = "new-srv";
        cred.User = "new-usr";
        cred.Server.Should().Be("new-srv");
        cred.User.Should().Be("new-usr");
    }

    [Fact]
    public void ConnectionString_Default_IsEmpty()
    {
        new ServerCredential().ConnectionString.Should().BeEmpty();
    }

    [Fact]
    public void RemoteFields_DefaultsAreEmpty_AndHasRemoteCredentialIsFalse()
    {
        var cred = new ServerCredential();
        cred.RemoteUser.Should().BeEmpty();
        cred.RemotePassword.Should().BeEmpty();
        cred.HasRemoteCredential.Should().BeFalse();
    }

    [Fact]
    public void HasRemoteCredential_TrueOnlyWhenBothFieldsSet()
    {
        // User allein reicht nicht (Windows-Passwort fehlt).
        var userOnly = new ServerCredential(RemoteUser: "DMZ\\svc");
        userOnly.HasRemoteCredential.Should().BeFalse();

        // Passwort allein reicht auch nicht (User fehlt).
        var pwOnly = new ServerCredential(RemotePassword: "p");
        pwOnly.HasRemoteCredential.Should().BeFalse();

        var both = new ServerCredential(RemoteUser: "DMZ\\svc", RemotePassword: "p");
        both.HasRemoteCredential.Should().BeTrue();
    }
}
