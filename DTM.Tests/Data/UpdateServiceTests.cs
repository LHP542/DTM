using DTM.Data.Updater;
using FluentAssertions;
using Xunit;
using SystemFile = System.IO.File;

namespace DTM.Tests.Data;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("2.0.0",                         2, 0, 0)]
    [InlineData("1.1.0",                         1, 1, 0)]
    [InlineData("1.0.4",                         1, 0, 4)]
    [InlineData("2.0.0+abcdef0",                 2, 0, 0)]                  // stable mit Git-Hash
    [InlineData("2.0.1-alpha.0.5+90fe0ba",       2, 0, 1)]                  // pre-release: hier war der Bug
    [InlineData("3.5.7-rc.2+sha",                3, 5, 7)]
    [InlineData("1.2.3-alpha",                   1, 2, 3)]                  // pre-release ohne Build-Metadata
    [InlineData("1.2",                           1, 2, -1)]                 // Version-Standardformat (-1 = build ungesetzt)
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
    [InlineData("not-a-version")]
    [InlineData("-alpha")]
    [InlineData("xx.yy.zz")]
    public void ParseInformationalVersion_FallbackOnInvalidInput(string input)
    {
        var v = UpdateService.ParseInformationalVersion(input);
        v.Should().Be(new Version(1, 0, 0));
    }

    [Fact]
    public async Task LoadReleaseNotesAsync_FiltersByRange_AndSortsDescending()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dtm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string json = """
            [
              { "version": "1.5.0", "date": "2026-01-01", "notes": ["A"], "modulesChanged": [] },
              { "version": "2.0.0", "date": "2026-06-01", "notes": ["B"], "modulesChanged": ["FOC-SQL"] },
              { "version": "2.1.0", "date": "2026-06-27", "notes": ["C"], "modulesChanged": ["MSSQL"] },
              { "version": "2.2.0", "date": "2026-09-01", "notes": ["D"], "modulesChanged": [] }
            ]
            """;
            await SystemFile.WriteAllTextAsync(Path.Combine(dir, "release-notes.json"), json);

            var notes = await UpdateService.LoadReleaseNotesAsync(dir, new Version(1, 5, 0), new Version(2, 1, 0));

            notes.Should().HaveCount(2);
            notes[0].Version.Should().Be("2.1.0");
            notes[1].Version.Should().Be("2.0.0");
            notes[0].ModulesChanged.Should().Contain("MSSQL");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task LoadReleaseNotesAsync_ReturnsEmpty_WhenFileMissing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dtm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var notes = await UpdateService.LoadReleaseNotesAsync(dir, new Version(1, 0, 0), new Version(2, 0, 0));
            notes.Should().BeEmpty();
        }
        finally { Directory.Delete(dir, true); }
    }
}
