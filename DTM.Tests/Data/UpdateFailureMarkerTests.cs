using DTM.Updater;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Data;

/// <summary>
/// Die Regel, die den Update-Kreisel durchbricht.
///
/// <para>Hintergrund: schlägt das Ersetzen der Dateien fehl, startet das
/// Austausch-Skript die alte Version wieder. Die prüft auf Updates, findet
/// dasselbe Paket, lädt es, scheitert erneut — am 2026-09-24 elfmal
/// hintereinander. Die Entscheidung, den automatischen Hinweis dann einmal zu
/// überspringen, steckt in dieser einen Funktion und ist deshalb ohne
/// Dateizugriff prüfbar.</para>
/// </summary>
public class UpdateFailureMarkerTests
{
    private static Version V(string s) => Version.Parse(s);

    [Fact]
    public void NoMarker_NothingIsSuppressed()
    {
        UpdateFailureMarker.ShouldSuppress(null, V("2.3.14"), V("2.4.0"))
            .Should().BeFalse("ohne vorherigen Fehlschlag gibt es nichts zu unterdrücken");
    }

    [Fact]
    public void SameVersionFailedBefore_IsSuppressed()
    {
        // Der Kreisel-Fall: 2.4.0 ist gescheitert, es läuft weiterhin 2.3.14,
        // und angeboten wird wieder 2.4.0.
        UpdateFailureMarker.ShouldSuppress(V("2.4.0"), V("2.3.14"), V("2.4.0"))
            .Should().BeTrue();
    }

    [Fact]
    public void NewerVersionAvailable_IsOfferedAnyway()
    {
        // 2.4.0 ist gescheitert, inzwischen liegt 2.4.1 bereit. Die ist einen
        // Versuch wert — vielleicht behebt gerade sie den Fehler.
        UpdateFailureMarker.ShouldSuppress(V("2.4.0"), V("2.3.14"), V("2.4.1"))
            .Should().BeFalse();
    }

    [Fact]
    public void UpdateSucceededEventually_MarkerIsStale()
    {
        // Der Austausch hat es doch geschafft — oder jemand hat von Hand
        // kopiert. Dann darf die alte Notiz nichts mehr blockieren.
        UpdateFailureMarker.ShouldSuppress(V("2.4.0"), V("2.4.0"), V("2.5.0"))
            .Should().BeFalse();
        UpdateFailureMarker.ShouldSuppress(V("2.4.0"), V("2.5.0"), V("2.6.0"))
            .Should().BeFalse();
    }

    [Fact]
    public void OlderFailureDoesNotBlockTheNextRelease()
    {
        // Vor Monaten ist 2.2.0 gescheitert, die Notiz blieb liegen. Sie darf
        // 2.4.0 nicht im Weg stehen.
        UpdateFailureMarker.ShouldSuppress(V("2.2.0"), V("2.3.14"), V("2.4.0"))
            .Should().BeFalse();
    }

    [Fact]
    public void MarkerPath_IsBesideTheOtherConfiguration()
    {
        // Bewusst nicht im Programmverzeichnis: dorthin schreibt der Austausch
        // selbst, und eine Notiz, die die nächste Kopie überschreibt, hilft
        // niemandem.
        UpdateFailureMarker.Path.Should()
            .Contain("DTM")
            .And.EndWith("letztes-update-fehlgeschlagen.txt");
    }
}
