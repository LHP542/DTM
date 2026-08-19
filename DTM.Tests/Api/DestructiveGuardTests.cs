using System.Reflection;
using System.Windows.Input;
using DTM.Data.Api;
using DTM.ViewModels;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Api;

public class DestructiveGuardTests
{
    [Theory]
    [InlineData("Backup")]
    [InlineData("BackupCommand")]
    [InlineData("backup")]
    [InlineData("RestoreSnapshot")]
    [InlineData("RemoveSnapshotCommand")]
    [InlineData("RunShrinkLog")]
    [InlineData("OlvmRemoveSnapshot")]
    public void IsDestructiveCommand_RecognisesWritingActions(string name)
    {
        DestructiveGuard.IsDestructiveCommand(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("ManageConnections")]
    [InlineData("ShowSessions")]
    [InlineData("CheckClusterHealth")]
    [InlineData("OpenBackupBrowser")]
    [InlineData("OpenDbConfiguration")]
    [InlineData("RunCheckDb")]        // DBCC CHECKDB ist lesend
    public void IsDestructiveCommand_AllowsReadOnlyActions(string name)
    {
        DestructiveGuard.IsDestructiveCommand(name).Should().BeFalse();
    }

    [Fact]
    public void IsDestructiveCommand_EmptyName_IsFalse()
    {
        DestructiveGuard.IsDestructiveCommand(string.Empty).Should().BeFalse();
    }

    [Theory]
    [InlineData("ConfirmButton")]
    [InlineData("RestoreButton")]
    [InlineData("CloseSessionsButton")]
    public void IsDestructiveElement_BlocksConfirmationButtons(string name)
    {
        DestructiveGuard.IsDestructiveElement(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("CancelButton")]
    [InlineData("CloseButton")]
    [InlineData("UpdateCheckButton")]
    public void IsDestructiveElement_AllowsHarmlessButtons(string name)
    {
        DestructiveGuard.IsDestructiveElement(name).Should().BeFalse();
    }

    /// <summary>
    /// Der eigentliche Wert dieser Testdatei: die Sperrliste ist von Hand
    /// gepflegt, das MainWindowViewModel waechst aber weiter. Dieser Test
    /// zwingt dazu, bei jedem NEUEN Command bewusst zu entscheiden, ob er
    /// schreibend wirkt — sonst rutscht eine destruktive Aktion still in den
    /// Nur-Beobachten-Modus.
    ///
    /// Faellt der Test um, gehoert der neue Command entweder in
    /// <see cref="DestructiveGuard"/> oder in die Liste unten.
    /// </summary>
    [Fact]
    public void EveryMainWindowCommand_IsClassified()
    {
        // Bewusst als unkritisch eingestufte Commands (lesend oder reine
        // Dialog-/Navigations-Aktionen).
        HashSet<string> knownHarmless = new(StringComparer.OrdinalIgnoreCase)
        {
            "ManageConnections",
            "ShowSessions",
            "CheckClusterHealth",
            "OpenBackupBrowser",
            "OpenDbConfiguration",
            "RunCheckDb",
        };

        List<string> commands = typeof(MainWindowViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
            .Select(p => p.Name.EndsWith("Command", StringComparison.Ordinal)
                ? p.Name[..^"Command".Length]
                : p.Name)
            .ToList();

        commands.Should().NotBeEmpty("das ViewModel muss Commands exponieren");

        List<string> unclassified = commands
            .Where(c => !DestructiveGuard.IsDestructiveCommand(c) && !knownHarmless.Contains(c))
            .ToList();

        unclassified.Should().BeEmpty(
            "jeder Command muss in DestructiveGuard oder in knownHarmless stehen — "
            + "sonst waere er ueber die REST-API ausloesbar, ohne dass jemand darueber "
            + "nachgedacht hat. Nicht eingeordnet: {0}", string.Join(", ", unclassified));
    }

    /// <summary>
    /// Gegenrichtung: ein Eintrag in der Sperrliste, den es am ViewModel nicht
    /// (mehr) gibt, ist toter Ballast und deutet auf eine Umbenennung hin.
    /// </summary>
    [Fact]
    public void EveryBlockedCommand_ExistsOnViewModel()
    {
        HashSet<string> vmCommands = typeof(MainWindowViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
            .Select(p => p.Name.EndsWith("Command", StringComparison.Ordinal)
                ? p.Name[..^"Command".Length]
                : p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> orphans = DestructiveGuard.KnownDestructiveCommands
            .Where(c => !vmCommands.Contains(c))
            .ToList();

        orphans.Should().BeEmpty(
            "die Sperrliste darf keine Commands enthalten, die es nicht mehr gibt "
            + "(Hinweis auf eine Umbenennung). Verwaist: {0}", string.Join(", ", orphans));
    }
}
