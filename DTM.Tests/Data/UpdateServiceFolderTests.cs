using System.Text.Json;
using DTM.Updater;
using FluentAssertions;
using Xunit;
using SystemFile = System.IO.File;

namespace DTM.Tests.Data;

/// <summary>
/// Der Ordner-Kanal des <see cref="UpdateService"/> — seit 2026-08-25 der
/// Regelweg, weil GitHub aus dem Firmennetz nicht mehr erreichbar ist.
///
/// <para>Bewusst NICHT geprueft wird, ob ein Update angeboten wird: das
/// haengt an der Assembly-Version des Testhosts und wuerde je nach Tag
/// unterschiedlich ausfallen. Der Versionsvergleich selbst steckt in
/// <see cref="UpdateChannel.Normalize"/> und ist dort abgedeckt.</para>
/// </summary>
public class UpdateServiceFolderTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "dtm-updatesvc-" + Guid.NewGuid().ToString("N"));

    public UpdateServiceFolderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EmptyChannel_UsesNetworkFolder()
    {
        using var svc = new UpdateService(null);

        svc.Channel.Should().Be(UpdateChannel.DefaultFolder);
        svc.UsesFolderChannel.Should().BeTrue("der Regelweg ist das Rollout-Verzeichnis");
    }

    [Fact]
    public void HttpsChannel_SwitchesToGitHub()
    {
        using var svc = new UpdateService("https://api.github.com/repos/LHP542/DTM/releases/latest");

        svc.UsesFolderChannel.Should().BeFalse();
    }

    /// <summary>
    /// Der Test, der den CI-Fehler gefangen haette: unter Linux liegt das
    /// Temp-Verzeichnis unter <c>/tmp/…</c>, und absolute Unix-Pfade galten
    /// nicht als Ordner. Die uebrigen Tests hier liefen dadurch still gegen
    /// GitHub und verglichen echte Release-Daten mit ihren Erwartungen.
    /// </summary>
    [Fact]
    public void TempFolderChannel_IsRecognisedAsFolder()
    {
        using var svc = new UpdateService(_dir, isWindows: true);

        svc.UsesFolderChannel.Should().BeTrue(
            "ein absoluter Pfad ist ein Ordner — auf jeder Plattform");
    }

    [Fact]
    public async Task CheckForUpdate_FindsNewestPackageInFolder()
    {
        SystemFile.WriteAllText(Path.Combine(_dir, "DTM-v9.9.8-windows.zip"), "x");
        SystemFile.WriteAllText(Path.Combine(_dir, "DTM-v9.9.9-windows.zip"), "x");
        SystemFile.WriteAllText(Path.Combine(_dir, "DTM-v9.9.9-x86_64.AppImage"), "x");

        using var svc = new UpdateService(_dir, isWindows: true);
        UpdateCheckResult? result = await svc.CheckForUpdateAsync(ct: TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result!.Latest.Should().Be(new Version(9, 9, 9));
        // Der Ordner wird als "Release-Seite" gemeldet — Explorer statt Browser.
        result.ReleaseUrl.Should().Be(_dir);
        result.AssetUrl.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckForUpdate_EmptyFolder_ReturnsNull()
    {
        using var svc = new UpdateService(_dir, isWindows: true);

        UpdateCheckResult? result = await svc.CheckForUpdateAsync(ct: TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdate_MissingFolder_ReturnsNullWithoutThrowing()
    {
        // Notebook ohne Netzlaufwerk: kein Fehler, nur kein Update.
        using var svc = new UpdateService(Path.Combine(_dir, "nicht-da"), isWindows: true);

        UpdateCheckResult? result = await svc.CheckForUpdateAsync(ct: TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadReleaseNotes_ReadsFileFromFolderAndFiltersRange()
    {
        var notes = new[]
        {
            new { version = "2.3.9",  notes = new[] { "alt" },  modulesChanged = Array.Empty<string>() },
            new { version = "2.3.11", notes = new[] { "neu" },  modulesChanged = new[] { "MSSQL" } },
            new { version = "2.4.0",  notes = new[] { "zu neu" }, modulesChanged = Array.Empty<string>() },
        };
        SystemFile.WriteAllText(
            Path.Combine(_dir, UpdateChannel.ReleaseNotesFileName),
            JsonSerializer.Serialize(notes));

        using var svc = new UpdateService(_dir, isWindows: true);
        IReadOnlyList<ReleaseNote> result = await svc.LoadReleaseNotesAsync(
            new Version(2, 3, 10), new Version(2, 3, 11),
            TestContext.Current.CancellationToken);

        result.Should().HaveCount(1);
        result[0].Version.Should().Be("2.3.11");
        // modulesChanged traegt den MSSQL-Banner im Update-Dialog — muss
        // ueber den Ordner-Kanal genauso ankommen wie ueber GitHub.
        result[0].ModulesChanged.Should().Contain("MSSQL");
    }

    [Fact]
    public async Task LoadReleaseNotes_MissingFile_ReturnsEmpty()
    {
        // Fehlende Notizen sind kein Grund, ein Update zu verschweigen.
        using var svc = new UpdateService(_dir, isWindows: true);

        IReadOnlyList<ReleaseNote> result = await svc.LoadReleaseNotesAsync(
            new Version(1, 0, 0), new Version(9, 0, 0),
            TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadReleaseNotes_BrokenJson_ReturnsEmpty()
    {
        SystemFile.WriteAllText(
            Path.Combine(_dir, UpdateChannel.ReleaseNotesFileName), "{ kaputt [[[");

        using var svc = new UpdateService(_dir, isWindows: true);
        IReadOnlyList<ReleaseNote> result = await svc.LoadReleaseNotesAsync(
            new Version(1, 0, 0), new Version(9, 0, 0),
            TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }
}
