using DTM.Updater;
using FluentAssertions;
using Xunit;
using SystemFile = System.IO.File;

namespace DTM.Tests.Data;

public class UpdateChannelTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "dtm-update-" + Guid.NewGuid().ToString("N"));

    public UpdateChannelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private void Touch(string name, DateTime? written = null)
    {
        string p = Path.Combine(_dir, name);
        SystemFile.WriteAllText(p, "x");
        if (written is { } w) SystemFile.SetLastWriteTimeUtc(p, w);
    }

    // --- Kanaltyp-Erkennung ---

    [Theory]
    [InlineData(@"\\samba01\542$\5424_IT-Basis-Dienste\MS-SQL\DTM")]
    [InlineData(@"C:\Rollout\DTM")]
    [InlineData("//samba01/share/DTM")]
    // Absolute Unix-Pfade: DTM laeuft als AppImage auch unter Linux. Fehlte
    // dieser Fall, galt dort jeder lokale Ordner als Adresse und der Check
    // lief still gegen GitHub — im CI so aufgeschlagen, weil die Tests mit
    // /tmp/… arbeiten.
    [InlineData("/srv/rollout/dtm")]
    [InlineData("/tmp/dtm-updatesvc-abc123")]
    [InlineData("/home/OsteL/Rollout")]
    public void LooksLikeFolder_RecognisesPaths(string channel)
    {
        UpdateChannel.LooksLikeFolder(channel).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://api.github.com/repos/LHP542/DTM/releases/latest")]
    [InlineData("http://example.invalid/dtm")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void LooksLikeFolder_RejectsUrlsAndEmpty(string? channel)
    {
        UpdateChannel.LooksLikeFolder(channel).Should().BeFalse();
    }

    [Fact]
    public void Resolve_EmptySetting_FallsBackToNetworkFolder()
    {
        UpdateChannel.Resolve(null).Should().Be(UpdateChannel.DefaultFolder);
        UpdateChannel.Resolve("   ").Should().Be(UpdateChannel.DefaultFolder);
    }

    [Fact]
    public void Resolve_KeepsExplicitSetting()
    {
        UpdateChannel.Resolve(@" C:\Rollout ").Should().Be(@"C:\Rollout");
    }

    // --- Version aus dem Dateinamen ---

    [Theory]
    [InlineData("DTM-v2.3.11-windows.zip", "2.3.11")]
    [InlineData("DTM-2.4.0-win-x64.zip", "2.4.0")]
    [InlineData("DTM-v2.3.11-x86_64.AppImage", "2.3.11")]
    [InlineData("DTM-v1.2.3.4-windows.zip", "1.2.3.4")]
    public void ParseVersionFromFileName_ReadsVersion(string name, string expected)
    {
        UpdateChannel.ParseVersionFromFileName(name).Should().Be(Version.Parse(expected));
    }

    [Theory]
    [InlineData("DTM-windows.zip")]
    [InlineData("readme.txt")]
    public void ParseVersionFromFileName_WithoutVersion_IsNull(string name)
    {
        UpdateChannel.ParseVersionFromFileName(name).Should().BeNull();
    }

    [Fact]
    public void Normalize_PadsToFourSegments()
    {
        // Ohne das gilt 2.3.11 (Revision -1) als kleiner als 2.3.11.0 und ein
        // Gleichstand wuerde faelschlich als Update angeboten.
        UpdateChannel.Normalize(new Version(2, 3, 11))
            .Should().Be(new Version(2, 3, 11, 0));
    }

    // --- Paketsuche im Ordner ---

    [Fact]
    public void FindNewestPackage_PicksHighestVersion()
    {
        Touch("DTM-v2.3.9-windows.zip");
        Touch("DTM-v2.3.11-windows.zip");
        Touch("DTM-v2.3.10-windows.zip");

        var (path, version) = UpdateChannel.FindNewestPackage(_dir, isWindows: true);

        version.Should().Be(new Version(2, 3, 11));
        Path.GetFileName(path!).Should().Be("DTM-v2.3.11-windows.zip");
    }

    [Fact]
    public void FindNewestPackage_IgnoresTimestamps()
    {
        // Der Kern der Regel: kopiert jemand ein aelteres Paket zurueck in den
        // Ordner, ist es die juengste Datei. Nach Zeitstempel sortiert waere
        // das ein "Update" auf eine aeltere Version.
        Touch("DTM-v2.3.11-windows.zip", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Touch("DTM-v2.3.9-windows.zip", DateTime.UtcNow);

        var (path, version) = UpdateChannel.FindNewestPackage(_dir, isWindows: true);

        version.Should().Be(new Version(2, 3, 11));
        Path.GetFileName(path!).Should().Be("DTM-v2.3.11-windows.zip");
    }

    [Fact]
    public void FindNewestPackage_Windows_IgnoresLinuxArtifacts()
    {
        Touch("DTM-v2.4.0-x86_64.AppImage");
        Touch("DTM-v2.4.0-linux.tar.gz");
        Touch("DTM-v2.3.11-windows.zip");

        var (path, version) = UpdateChannel.FindNewestPackage(_dir, isWindows: true);

        version.Should().Be(new Version(2, 3, 11));
        path.Should().EndWith(".zip");
    }

    [Fact]
    public void FindNewestPackage_Linux_PrefersAppImageOverTarGz()
    {
        // Gleiche Version, zwei Formate: das AppImage ersetzt sich selbst und
        // ist deshalb der bessere Weg.
        Touch("DTM-v2.3.11-linux.tar.gz");
        Touch("DTM-v2.3.11-x86_64.AppImage");

        var (path, _) = UpdateChannel.FindNewestPackage(_dir, isWindows: false);

        path.Should().EndWith(".AppImage");
    }

    [Fact]
    public void FindNewestPackage_Linux_FallsBackToTarGz()
    {
        Touch("DTM-v2.3.11-linux.tar.gz");

        var (path, _) = UpdateChannel.FindNewestPackage(_dir, isWindows: false);

        path.Should().EndWith(".tar.gz");
    }

    [Fact]
    public void FindNewestPackage_EmptyFolder_IsNull()
    {
        var (path, version) = UpdateChannel.FindNewestPackage(_dir, isWindows: true);

        path.Should().BeNull();
        version.Should().BeNull();
    }

    [Fact]
    public void FindNewestPackage_MissingFolder_IsNullAndDoesNotThrow()
    {
        // Notebook ohne Netzlaufwerk — Normalfall, kein Fehler.
        string missing = Path.Combine(_dir, "gibtsnicht");

        Action act = () => UpdateChannel.FindNewestPackage(missing, isWindows: true);

        act.Should().NotThrow();
        UpdateChannel.FindNewestPackage(missing, isWindows: true).Path.Should().BeNull();
    }

    [Fact]
    public void FindNewestPackage_SkipsFilesWithoutVersion()
    {
        Touch("DTM-windows.zip");
        Touch("DTM-v2.3.11-windows.zip");

        var (path, _) = UpdateChannel.FindNewestPackage(_dir, isWindows: true);

        Path.GetFileName(path!).Should().Be("DTM-v2.3.11-windows.zip");
    }
}
