using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;
using SystemFile = System.IO.File;

namespace DTM.Tests.Views;

/// <summary>
/// Weder der C#- noch der XAML-Übersetzer meldet einen Stil-Verweis ins Leere:
/// ein <c>{DynamicResource GibtsNichtBrush}</c> lässt die Eigenschaft still auf
/// ihrem Vorgabewert, und ein <c>Classes="accent"</c> ohne passenden Stil sieht
/// aus wie ein gewöhnlicher Knopf. Beides ist in DTM real passiert — der tote
/// <c>accent</c>-Verweis im UpdatePromptWindow stand zwei Ausgaben lang im Repo.
///
/// <para>Diese Prüfungen liefen bisher als Einmal-Skript nach dem
/// Paletten-Umbau. Als Test laufen sie bei jedem Commit — genau dann, wenn
/// jemand einen Schlüssel umbenennt und drei von zweihundert Stellen
/// übersieht.</para>
/// </summary>
public class XamlResourceTests
{
    private static readonly Regex KeyDefinition = new(@"x:Key=""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ResourceUse =
        new(@"\{(?:Dynamic|Static)Resource\s+([A-Za-z0-9_.]+)\s*\}", RegexOptions.Compiled);
    private static readonly Regex SelectorDefinition = new(@"Selector=""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ClassUse = new(@"Classes=""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ColorLiteral = new(@"""#[0-9A-Fa-f]{6,8}""", RegexOptions.Compiled);

    /// <summary>
    /// Alle <c>.axaml</c> der App. Das Repo-Wurzelverzeichnis wird vom
    /// Ausgabeordner aus über die <c>.slnx</c> gesucht — greift die Auflösung
    /// daneben, findet der Test nichts und wäre wertlos, deshalb prüft jeder
    /// Test zusätzlich, dass überhaupt Dateien gefunden wurden.
    /// </summary>
    private static IReadOnlyList<string> XamlFiles()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;

        dir.Should().NotBeNull("die .slnx markiert das Repo-Wurzelverzeichnis");

        string app = Path.Combine(dir!.FullName, "DTM");
        Directory.Exists(app).Should().BeTrue("das App-Verzeichnis muss neben der .slnx liegen");

        List<string> files = Directory
            .EnumerateFiles(app, "*.axaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        files.Should().NotBeEmpty("ohne gefundene XAML-Dateien prüft dieser Test nichts");
        return files;
    }

    [Fact]
    public void EveryReferencedResourceKey_IsDefined()
    {
        var defined = new HashSet<string>(StringComparer.Ordinal);
        var used = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string file in XamlFiles())
        {
            string text = SystemFile.ReadAllText(file);
            foreach (Match m in KeyDefinition.Matches(text))
                defined.Add(m.Groups[1].Value);
            foreach (Match m in ResourceUse.Matches(text))
                used.TryAdd(m.Groups[1].Value, Path.GetFileName(file));
        }

        // Schlüssel des Frameworks (Fluent-Theme) definiert DTM nicht selbst.
        List<string> missing = used.Keys
            .Where(k => !k.StartsWith("System", StringComparison.Ordinal))
            .Where(k => !defined.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "ein fehlender Schlüssel wirft nicht, sondern lässt die Eigenschaft still auf ihrem "
            + "Vorgabewert. Nicht definiert: {0}",
            string.Join(", ", missing.Select(k => $"{k} (in {used[k]})")));
    }

    [Fact]
    public void EveryUsedStyleClass_HasASelector()
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var used = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string file in XamlFiles())
        {
            string text = SystemFile.ReadAllText(file);

            foreach (Match m in SelectorDefinition.Matches(text))
                foreach (Match c in Regex.Matches(m.Groups[1].Value, @"\.([A-Za-z0-9_-]+)"))
                    declared.Add(c.Groups[1].Value);

            foreach (Match m in ClassUse.Matches(text))
                foreach (string cls in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    used.TryAdd(cls, Path.GetFileName(file));
        }

        List<string> orphans = used.Keys
            .Where(c => !declared.Contains(c))
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        orphans.Should().BeEmpty(
            "ein Classes-Verweis ohne passenden Stil ist gültiges XAML und rendert als "
            + "gewöhnliches Control. Ohne Selektor: {0}",
            string.Join(", ", orphans.Select(c => $"{c} (in {used[c]})")));
    }

    [Fact]
    public void ResourceKeys_AreUnique()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicates = new List<string>();

        foreach (string file in XamlFiles())
        {
            foreach (Match m in KeyDefinition.Matches(SystemFile.ReadAllText(file)))
            {
                string key = m.Groups[1].Value;
                if (!seen.TryAdd(key, Path.GetFileName(file)))
                    duplicates.Add($"{key} ({seen[key]} + {Path.GetFileName(file)})");
            }
        }

        duplicates.Should().BeEmpty(
            "doppelte Schlüssel fallen erst beim Laden des Wörterbuchs auf, nicht beim Übersetzen: {0}",
            string.Join(", ", duplicates));
    }

    /// <summary>
    /// Farben gehören ausschließlich in die Palette in <c>App.axaml</c>. Eine
    /// Ausnahme bleibt bewusst: der Buy-me-a-coffee-Knopf soll in den
    /// Wiedererkennungsfarben der Marke stehen und liegt deshalb außerhalb der
    /// Kroste-Palette — dort aber ebenfalls als benannter Schlüssel.
    /// </summary>
    [Fact]
    public void NoColorLiterals_OutsideThePalette()
    {
        var offenders = new List<string>();

        foreach (string file in XamlFiles())
        {
            if (Path.GetFileName(file) == "App.axaml") continue;
            if (ColorLiteral.IsMatch(SystemFile.ReadAllText(file)))
                offenders.Add(Path.GetFileName(file));
        }

        offenders.Should().BeEmpty(
            "Farbwerte gehören als benannter Schlüssel in die Palette, nicht ins Fenster-XAML. "
            + "Betroffen: {0}", string.Join(", ", offenders));
    }
}
