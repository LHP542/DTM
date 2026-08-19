using DTM.Config;
using FluentAssertions;
using Xunit;
using SystemFile = System.IO.File;

namespace DTM.Tests.Config;

/// <summary>
/// Direkte Tests der Datei-Primitiven. Die Store-Tests decken den Weg ueber
/// Load/Save ab; hier geht es um die Randfaelle des Helpers selbst.
/// </summary>
public class JsonFileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "dtm-jsonstore-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string PathIn(string name) => Path.Combine(_dir, name);

    [Fact]
    public void WriteAtomic_CreatesMissingDirectory()
    {
        string target = Path.Combine(_dir, "tief", "verschachtelt", "data.json");

        JsonFileStore.WriteAtomic(target, "{}");

        SystemFile.Exists(target).Should().BeTrue();
    }

    [Fact]
    public void WriteAtomic_WritesExactContent()
    {
        string target = PathIn("data.json");

        JsonFileStore.WriteAtomic(target, "{\"a\":1}");

        SystemFile.ReadAllText(target).Should().Be("{\"a\":1}");
    }

    [Fact]
    public void WriteAtomic_RemovesTempFile()
    {
        string target = PathIn("data.json");

        JsonFileStore.WriteAtomic(target, "{}");

        SystemFile.Exists(target + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void WriteAtomic_OverwritesShorterContent_WithoutLeftovers()
    {
        // Der eigentliche Grund fuer tmp+Move statt In-Place-Write: die neue
        // Datei ersetzt die alte vollstaendig, statt sie zu ueberschreiben.
        string target = PathIn("data.json");
        JsonFileStore.WriteAtomic(target, "AAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        JsonFileStore.WriteAtomic(target, "B");

        SystemFile.ReadAllText(target).Should().Be("B");
    }

    [Fact]
    public void Quarantine_MovesFileToBroken()
    {
        string target = PathIn("data.json");
        Directory.CreateDirectory(_dir);
        SystemFile.WriteAllText(target, "kaputt");

        JsonFileStore.Quarantine(target);

        SystemFile.Exists(target).Should().BeFalse();
        SystemFile.ReadAllText(target + ".broken").Should().Be("kaputt");
    }

    [Fact]
    public void Quarantine_OverwritesPreviousBrokenFile()
    {
        // Zweiter Defekt in Folge darf nicht an einer bereits existierenden
        // .broken-Datei scheitern — sonst bliebe die kaputte Datei liegen und
        // der naechste Save wuerde sie doch noch ueberschreiben.
        string target = PathIn("data.json");
        Directory.CreateDirectory(_dir);
        SystemFile.WriteAllText(target + ".broken", "alt");
        SystemFile.WriteAllText(target, "neu");

        JsonFileStore.Quarantine(target);

        SystemFile.ReadAllText(target + ".broken").Should().Be("neu");
    }

    [Fact]
    public void Quarantine_MissingFile_DoesNotThrow()
    {
        Directory.CreateDirectory(_dir);

        Action act = () => JsonFileStore.Quarantine(PathIn("gibtsnicht.json"));

        act.Should().NotThrow();
    }
}
