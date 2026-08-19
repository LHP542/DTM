using DTM.Diagnostics;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Diagnostics;

/// <summary>
/// Der Guard arbeitet auf echten Named Pipes — die Tests belegen deshalb je
/// einen eindeutigen App-Namen, damit parallele Testlaeufe und eine echte
/// laufende DTM-Instanz sich nicht in die Quere kommen.
/// </summary>
public class SingleInstanceGuardTests
{
    private static string UniqueName() => "DTM-Test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void TryClaim_FirstInstance_Succeeds()
    {
        using var guard = new SingleInstanceGuard(UniqueName());

        guard.TryClaim().Should().BeTrue();
    }

    [Fact]
    public void TryClaim_SecondInstance_Fails()
    {
        string name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        first.TryClaim().Should().BeTrue();

        using var second = new SingleInstanceGuard(name);
        second.TryClaim().Should().BeFalse("die erste Instanz haelt die Pipe");
    }

    [Fact]
    public void TryClaim_DifferentAppNames_DoNotBlockEachOther()
    {
        using var a = new SingleInstanceGuard(UniqueName());
        using var b = new SingleInstanceGuard(UniqueName());

        a.TryClaim().Should().BeTrue();
        b.TryClaim().Should().BeTrue();
    }

    [Fact]
    public void TryClaim_AfterDispose_SucceedsAgain()
    {
        // Wichtig fuer den Update-Pfad: DTM beendet sich, das Installer-Skript
        // startet die neue Version sofort — die Pipe muss dann frei sein.
        string name = UniqueName();
        var first = new SingleInstanceGuard(name);
        first.TryClaim().Should().BeTrue();
        first.Dispose();

        using var second = new SingleInstanceGuard(name);
        second.TryClaim().Should().BeTrue();
    }

    [Fact]
    public void NotifyPrimary_RaisesActivationRequestedOnFirstInstance()
    {
        string name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        first.TryClaim().Should().BeTrue();

        using var signal = new ManualResetEventSlim(false);
        first.ActivationRequested += (_, _) => signal.Set();

        using var second = new SingleInstanceGuard(name);
        second.TryClaim().Should().BeFalse();
        second.NotifyPrimary();

        signal.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .Should().BeTrue("die Erstinstanz muss den Aktivierungswunsch empfangen");
    }

    [Fact]
    public void NotifyPrimary_HandlesSecondActivation()
    {
        // Der Listener muss nach der ersten Verbindung weiterlauschen —
        // sonst zeigt nur der erste Zweitstart das Fenster an.
        string name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        first.TryClaim().Should().BeTrue();

        int count = 0;
        using var signal = new ManualResetEventSlim(false);
        first.ActivationRequested += (_, _) =>
        {
            if (Interlocked.Increment(ref count) == 2) signal.Set();
        };

        using (var second = new SingleInstanceGuard(name)) second.NotifyPrimary();
        using (var third = new SingleInstanceGuard(name)) third.NotifyPrimary();

        signal.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .Should().BeTrue("beide Aktivierungen muessen ankommen");
    }

    [Fact]
    public void NotifyPrimary_WithoutRunningInstance_DoesNotThrow()
    {
        using var guard = new SingleInstanceGuard(UniqueName());

        Action act = guard.NotifyPrimary;

        act.Should().NotThrow("ein fehlgeschlagener Weckruf darf den Start nicht kippen");
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var guard = new SingleInstanceGuard(UniqueName());
        guard.TryClaim();

        Action act = () => { guard.Dispose(); guard.Dispose(); };

        act.Should().NotThrow();
    }
}
