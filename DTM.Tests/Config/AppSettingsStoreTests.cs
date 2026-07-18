using DTM.Data.Config;
using FluentAssertions;
using Xunit;
using SystemFile = System.IO.File;

namespace DTM.Tests.Config;

[Collection("serial")]
public class AppSettingsStoreTests : IDisposable
{
    private readonly string _tmp = Path.Combine(
        Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
    private readonly string _original;

    public AppSettingsStoreTests()
    {
        _original = AppSettingsStore._path;
        AppSettingsStore._path = _tmp;
    }

    public void Dispose()
    {
        if (SystemFile.Exists(_tmp)) SystemFile.Delete(_tmp);
        AppSettingsStore._path = _original;
    }

    [Fact]
    public void LoadFocSql_NoFile_ReturnsDefaults()
    {
        var cfg = AppSettingsStore.LoadFocSql();
        cfg.SambaSource.Should().BeEmpty();
        cfg.ModulePath.Should().BeEmpty();
    }

    [Fact]
    public void SaveFocSql_Then_Load_RoundTrip()
    {
        AppSettingsStore.SaveFocSql(new FocSqlConfig { SambaSource = @"\\srv\share", ModulePath = @"C:\mod" });
        var cfg = AppSettingsStore.LoadFocSql();
        cfg.SambaSource.Should().Be(@"\\srv\share");
        cfg.ModulePath.Should().Be(@"C:\mod");
    }

    [Fact]
    public void SaveFocSql_Then_Load_PreservesBothFields()
    {
        AppSettingsStore.SaveFocSql(new FocSqlConfig { SambaSource = "samba", ModulePath = "mod" });
        var cfg = AppSettingsStore.LoadFocSql();
        cfg.SambaSource.Should().Be("samba");
        cfg.ModulePath.Should().Be("mod");
    }

    [Fact]
    public void SaveFocSql_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_tmp)!);
        SystemFile.WriteAllText(_tmp, "totally not json");
        var cfg = AppSettingsStore.LoadFocSql();
        cfg.SambaSource.Should().BeEmpty();
        cfg.ModulePath.Should().BeEmpty();
    }

    [Fact]
    public void SaveFocSql_CreatesDirectory_IfNotExists()
    {
        string nested = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "sub", "settings.json");
        AppSettingsStore._path = nested;
        try
        {
            AppSettingsStore.SaveFocSql(new FocSqlConfig { SambaSource = "x" });
            SystemFile.Exists(nested).Should().BeTrue();
        }
        finally
        {
            AppSettingsStore._path = _tmp;
            try
            {
                if (SystemFile.Exists(nested)) SystemFile.Delete(nested);
                string? guidDir = Path.GetDirectoryName(Path.GetDirectoryName(nested));
                if (guidDir != null && Directory.Exists(guidDir))
                    Directory.Delete(guidDir, recursive: true);
            }
            catch (IOException) { }
        }
    }
}
