using DTM.Updater;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Data;

/// <summary>
/// Seit v2.3.0-prep: UpdateService gegen GitHub Releases (Klemmbrett-Muster).
/// GitHub-API-Aufrufe + Release-Notes-Raw-Download sind Integration-Tests
/// (brauchen Netzwerk), daher hier nur die statischen Version-Parser-
/// Regeln.
/// </summary>
public class UpdateServiceTests
{
    [Theory]
    [InlineData("2.0.0",                         2, 0, 0)]
    [InlineData("1.1.0",                         1, 1, 0)]
    [InlineData("1.0.4",                         1, 0, 4)]
    [InlineData("2.0.0+abcdef0",                 2, 0, 0)]              // stable mit Git-Hash
    [InlineData("2.0.1-alpha.0.5+90fe0ba",       2, 0, 1)]              // pre-release
    [InlineData("3.5.7-rc.2+sha",                3, 5, 7)]
    [InlineData("1.2.3-alpha",                   1, 2, 3)]              // pre-release ohne Build-Metadata
    [InlineData("v2.0.0",                        2, 0, 0)]              // GitHub-Tag-Prefix (v/V)
    [InlineData("V1.4.0",                        1, 4, 0)]
    [InlineData("1.2",                           1, 2, 0)]              // Build auf 0 normalisiert (Klemmbrett-Regel)
    public void ParseInformationalVersion_ExtractsCorrectVersion(
        string input, int major, int minor, int build)
    {
        var v = UpdateService.ParseInformationalVersion(input);
        v.Major.Should().Be(major);
        v.Minor.Should().Be(minor);
        v.Build.Should().Be(build);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-version")]
    [InlineData("-alpha")]
    [InlineData("xx.yy.zz")]
    public void ParseInformationalVersion_FallbackOnInvalidInput(string? input)
    {
        var v = UpdateService.ParseInformationalVersion(input);
        v.Should().Be(new Version(1, 0, 0));
    }
}
