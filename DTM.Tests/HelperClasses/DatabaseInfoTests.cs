using FluentAssertions;
using Xunit;

namespace DTM.Tests.HelperClasses;

public class DatabaseInfoTests
{
    private static DatabaseInfo Make(string name = "DB", DatabaseStatus status = DatabaseStatus.up,
        string? fqdn = "srv.local")
        => new() { Name = name, Id = "1", FQDN = fqdn, Status = status };

    [Fact]
    public void DatabaseStatus_HasDownUpTransitional()
    {
        var values = Enum.GetValues<DatabaseStatus>();
        values.Should().Contain(DatabaseStatus.down);
        values.Should().Contain(DatabaseStatus.up);
        values.Should().Contain(DatabaseStatus.transitional);
    }

    [Fact]
    public void Record_EqualValues_AreEqual()
    {
        var a = Make("MyDB");
        var b = Make("MyDB");
        a.Should().Be(b);
    }

    [Fact]
    public void Record_DifferentName_NotEqual()
    {
        Make("DB1").Should().NotBe(Make("DB2"));
    }

    [Fact]
    public void Record_DifferentStatus_NotEqual()
    {
        Make(status: DatabaseStatus.up).Should().NotBe(Make(status: DatabaseStatus.down));
    }

    [Fact]
    public void FQDN_CanBeNull()
    {
        Make(fqdn: null).FQDN.Should().BeNull();
    }
}
