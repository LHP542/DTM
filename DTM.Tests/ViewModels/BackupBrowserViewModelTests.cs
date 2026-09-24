using DTM.Config;
using DTM.Data.MariaDb;
using DTM.Data.Terminal;
using DTM.MariaDb;
using DTM.ViewModels;
using FluentAssertions;
using Xunit;
using SystemFile = System.IO.File;

namespace DTM.Tests.ViewModels;

/// <summary>
/// Der Backup-Browser bedient drei Quellen mit einer Ansicht. Der
/// MariaDB-Zweig ist der einzige, der sich ohne Server prüfen lässt — er
/// liest nur das Dateisystem. Genau das macht ihn hier interessant: die
/// Umrechnung auf die gemeinsame Anzeige-Struktur passiert in der Klasse und
/// wäre sonst nirgends abgedeckt.
/// </summary>
public class BackupBrowserViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "dtm-browser-" + Guid.NewGuid().ToString("N"));

    public BackupBrowserViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private MariaDbBackupService Backups() =>
        new(new MariaDb_Connector(new ServerCredential("db01", "root", "geheim")),
            new MariaDbSettings { BackupRoot = _dir });

    /// <summary>
    /// Der FOC-SQL-Dienst wird im MariaDB-Zweig nicht angefasst; eine leere
    /// Instanz reicht und startet keinen PowerShell-Runspace.
    /// </summary>
    private static BackupBrowserViewModel NewViewModel() => new(new BackupBrowserService());

    [Fact]
    public async Task LoadAsync_MariaDb_ShowsDumpsNewestFirst()
    {
        MariaDbBackupService svc = Backups();
        string dir = svc.BackupDirectoryFor("kunden");
        Directory.CreateDirectory(dir);

        string alt = Path.Combine(dir, "kunden-20200101_1000.sql");
        string neu = Path.Combine(dir, "kunden-20250101_1000.sql");
        SystemFile.WriteAllBytes(alt, new byte[10]);
        SystemFile.WriteAllBytes(neu, new byte[2048]);
        SystemFile.SetCreationTimeUtc(alt, new DateTime(2020, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        SystemFile.SetCreationTimeUtc(neu, new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc));

        BackupBrowserViewModel vm = NewViewModel();
        await vm.LoadAsync("kunden", server: "db01", odbcActions: null, mariaDbBackups: svc);

        vm.ErrorMessage.Should().BeNull();
        vm.HasBackups.Should().BeTrue();
        vm.Backups.Should().HaveCount(2);
        vm.Backups[0].Name.Should().Be("kunden-20250101_1000.sql");
        vm.Backups[0].SizeBytes.Should().Be(2048);
        vm.Backups[0].Path.Should().Be(neu);
        vm.SelectedBackup.Should().BeSameAs(vm.Backups[0]);
    }

    [Fact]
    public async Task LoadAsync_MariaDb_WithoutDumps_ShowsEmptyListWithoutError()
    {
        BackupBrowserViewModel vm = NewViewModel();

        await vm.LoadAsync("kunden", server: "db01", odbcActions: null, mariaDbBackups: Backups());

        vm.HasBackups.Should().BeFalse();
        vm.Backups.Should().BeEmpty();
        vm.ErrorMessage.Should().BeNull("ein leeres Verzeichnis ist kein Fehler");
        vm.IsLoading.Should().BeFalse();
    }

    /// <summary>
    /// Der Hinweis im Bestätigungs-Dialog muss zur Quelle passen: MSSQL
    /// beendet die Sessions vor dem Restore, der MariaDB-Client nicht. Stünde
    /// dort derselbe Satz, wäre er bei MariaDB schlicht falsch.
    /// </summary>
    [Fact]
    public async Task RestoreNote_DependsOnSource()
    {
        BackupBrowserViewModel mssql = NewViewModel();
        mssql.RestoreNote.Should().Contain("beendet");

        BackupBrowserViewModel maria = NewViewModel();
        await maria.LoadAsync("kunden", server: "db01", odbcActions: null, mariaDbBackups: Backups());
        maria.RestoreNote.Should().Contain("nicht beendet");
    }
}
