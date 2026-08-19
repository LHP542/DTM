using DTM.Config;
using DTM.Data.Api;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Api;

public class ApiOptionsResolverTests
{
    private static AppLaunchOptions NoCli() => new();

    [Fact]
    public void Default_ApiIsOff()
    {
        var opts = ApiOptionsResolver.Resolve(new ApiSettings(), NoCli());

        opts.Enabled.Should().BeFalse("die API darf nur nach bewusster Entscheidung laufen");
        opts.AllowDestructive.Should().BeFalse();
    }

    [Fact]
    public void SettingsEnabled_TurnsApiOn()
    {
        var opts = ApiOptionsResolver.Resolve(
            new ApiSettings { Enabled = true, Port = 9000, BearerToken = "t" }, NoCli());

        opts.Enabled.Should().BeTrue();
        opts.Port.Should().Be(9000);
        opts.BearerToken.Should().Be("t");
    }

    [Fact]
    public void CliPort_TurnsApiOn_EvenWhenSettingsDisabled()
    {
        // Sonst muesste man fuer einen automatisierten Lauf immer zusaetzlich
        // die settings.json anfassen — genau das soll der CLI-Weg ersparen.
        var opts = ApiOptionsResolver.Resolve(
            new ApiSettings { Enabled = false },
            new AppLaunchOptions { ApiPortOverride = 8888 });

        opts.Enabled.Should().BeTrue();
        opts.Port.Should().Be(8888);
    }

    [Fact]
    public void CliOverridesSettings()
    {
        var opts = ApiOptionsResolver.Resolve(
            new ApiSettings { Enabled = true, Port = 1111, BearerToken = "aus-settings" },
            new AppLaunchOptions { ApiPortOverride = 2222, ApiTokenOverride = "aus-cli" });

        opts.Port.Should().Be(2222);
        opts.BearerToken.Should().Be("aus-cli");
    }

    [Fact]
    public void CliCanEnableDestructive()
    {
        var opts = ApiOptionsResolver.Resolve(
            new ApiSettings { Enabled = true },
            new AppLaunchOptions { ApiAllowDestructiveOverride = true });

        opts.AllowDestructive.Should().BeTrue();
    }

    [Fact]
    public void WithoutCliFlag_SettingsDecideDestructive()
    {
        var opts = ApiOptionsResolver.Resolve(
            new ApiSettings { Enabled = true, AllowDestructive = true }, NoCli());

        opts.AllowDestructive.Should().BeTrue();
    }
}

public class AppLaunchOptionsTests
{
    [Fact]
    public void Parse_ReadsAllApiArguments()
    {
        var o = AppLaunchOptions.Parse(
            ["--api-port", "8765", "--api-token", "geheim", "--api-allow-destructive", "--auto-shutdown-after", "5m"]);

        o.ApiPortOverride.Should().Be(8765);
        o.ApiTokenOverride.Should().Be("geheim");
        o.ApiAllowDestructiveOverride.Should().BeTrue();
        o.AutoShutdownAfter.Should().Be(TimeSpan.FromMinutes(5));
        o.RemainingArgs.Should().BeEmpty();
    }

    [Fact]
    public void Parse_PassesUnknownArgsThrough()
    {
        // Avalonia muss seine eigenen Flags noch sehen.
        var o = AppLaunchOptions.Parse(["--irgendwas", "wert", "--api-port", "8765"]);

        o.ApiPortOverride.Should().Be(8765);
        o.RemainingArgs.Should().BeEquivalentTo(["--irgendwas", "wert"]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("keinePortnummer")]
    public void Parse_InvalidPort_IsIgnored(string value)
    {
        AppLaunchOptions.Parse(["--api-port", value]).ApiPortOverride.Should().BeNull();
    }

    [Fact]
    public void Parse_PortWithoutValue_IsIgnored()
    {
        AppLaunchOptions.Parse(["--api-port"]).ApiPortOverride.Should().BeNull();
    }

    [Fact]
    public void Parse_NoArgs_LeavesEverythingUnset()
    {
        var o = AppLaunchOptions.Parse([]);

        o.ApiPortOverride.Should().BeNull();
        o.ApiTokenOverride.Should().BeNull();
        o.ApiAllowDestructiveOverride.Should().BeNull();
        o.AutoShutdownAfter.Should().BeNull();
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("2m", 120)]
    [InlineData("1h", 3600)]
    [InlineData("45", 45)]      // blanke Zahl = Sekunden
    public void TryParseDuration_AcceptsUnits(string input, int expectedSeconds)
    {
        AppLaunchOptions.TryParseDuration(input, out TimeSpan d).Should().BeTrue();
        d.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0s")]
    [InlineData("-5m")]
    public void TryParseDuration_RejectsNonsense(string input)
    {
        AppLaunchOptions.TryParseDuration(input, out _).Should().BeFalse();
    }
}
