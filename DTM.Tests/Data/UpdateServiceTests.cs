using System.Text.Json;
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

    // --- SelectAsset ---------------------------------------------------------
    // Regressionsschutz fuer den Windows-Self-Update-Bug: release.yml liefert
    // „DTM-vX.Y.Z-windows.zip" / „-linux.tar.gz", der Selector erwartete frueher
    // hart „win-x64"/„linux-x64" → unter Windows kein Asset → Self-Update
    // unmoeglich (AppImage matchte weiter, daher unter Linux nie aufgefallen).
    // Diese Tests decken BEIDE Namensschemata ab und laufen OS-unabhaengig,
    // weil die Plattform als Parameter injiziert wird.

    private static JsonElement Release(params string[] assetNames)
    {
        var items = assetNames.Select(n =>
            "{\"name\":\"" + n + "\",\"browser_download_url\":\"https://example.invalid/" + n + "\"}");
        var json = "{\"assets\":[" + string.Join(",", items) + "]}";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone(); // Clone: ueberlebt das Dispose des Documents
    }

    [Theory]
    [InlineData("DTM-v2.3.5-windows.zip")]  // DTM-release.yml (der reale Fehlerfall)
    [InlineData("DTM-v2.3.5-win-x64.zip")]  // Kroste-Standard-Schema
    public void SelectAsset_Windows_PicksZip_BothNamingSchemes(string zipName)
    {
        var release = Release(zipName, "DTM-v2.3.5-linux.tar.gz", "DTM-v2.3.5-x86_64.AppImage");
        var (name, url) = UpdateService.SelectAsset(release, isWindows: true);
        name.Should().Be(zipName);
        url.Should().Be("https://example.invalid/" + zipName);
    }

    [Theory]
    [InlineData("DTM-v2.3.5-linux.tar.gz")]     // DTM-release.yml
    [InlineData("DTM-v2.3.5-linux-x64.tar.gz")] // Kroste-Standard-Schema
    public void SelectAsset_Linux_FallsBackToTarGz_BothNamingSchemes(string tarName)
    {
        var release = Release("DTM-v2.3.5-windows.zip", tarName);
        var (name, url) = UpdateService.SelectAsset(release, isWindows: false);
        name.Should().Be(tarName);
        url.Should().Be("https://example.invalid/" + tarName);
    }

    [Fact]
    public void SelectAsset_Linux_PrefersAppImageOverTarGz()
    {
        var release = Release("DTM-v2.3.5-linux.tar.gz", "DTM-v2.3.5-x86_64.AppImage");
        var (name, _) = UpdateService.SelectAsset(release, isWindows: false);
        name.Should().Be("DTM-v2.3.5-x86_64.AppImage");
    }

    [Fact]
    public void SelectAsset_Windows_NoMatchingZip_ReturnsNull()
    {
        var release = Release("DTM-v2.3.5-linux.tar.gz", "DTM-v2.3.5-x86_64.AppImage");
        var (name, url) = UpdateService.SelectAsset(release, isWindows: true);
        name.Should().BeNull();
        url.Should().BeNull();
    }

    [Fact]
    public void SelectAsset_EmptyAssets_ReturnsNull()
    {
        var (name, url) = UpdateService.SelectAsset(Release(), isWindows: true);
        name.Should().BeNull();
        url.Should().BeNull();
    }
}
