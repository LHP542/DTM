using DTM.Data.Api;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Api;

/// <summary>
/// Der Vergleich ist die einzige Stelle zwischen "jemand kennt das Token" und
/// "jemand darf die DTM-Oberflaeche fernsteuern" — entsprechend explizit
/// getestet. Die Middleware selbst braeuchte einen HttpContext; die Logik
/// dahinter ist hier isoliert.
/// </summary>
public class ApiBearerAuthTests
{
    [Fact]
    public void FixedTimeEquals_IdenticalStrings_AreEqual()
    {
        ApiBearerAuth.FixedTimeEquals("geheim", "geheim").Should().BeTrue();
    }

    [Theory]
    [InlineData("geheim", "geheiM")]   // Gross-/Kleinschreibung zaehlt
    [InlineData("geheim", "geheim ")]  // Laenge zaehlt
    [InlineData("geheim", "")]
    [InlineData("", "geheim")]
    [InlineData("abc", "xyz")]
    public void FixedTimeEquals_Differences_AreRejected(string a, string b)
    {
        ApiBearerAuth.FixedTimeEquals(a, b).Should().BeFalse();
    }

    [Fact]
    public void FixedTimeEquals_EmptyStrings_AreEqual()
    {
        // Der leere Fall wird vorher in Enforce abgefangen (kein Token = 403);
        // hier geht es nur darum, dass der Vergleich selbst nicht ueberrascht.
        ApiBearerAuth.FixedTimeEquals(string.Empty, string.Empty).Should().BeTrue();
    }
}
