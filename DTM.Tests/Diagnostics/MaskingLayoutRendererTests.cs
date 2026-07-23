using DTM.Diagnostics;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Diagnostics;

/// <summary>
/// Regex-Regeln des masked-LayoutRenderers pruefen. Wir testen direkt
/// gegen die statische Mask-Methode, damit das Verhalten nicht vom
/// NLog-Rendering-Pipeline abhaengt (die kann Sonderzeichen im Message-
/// String eigenwillig interpretieren).
/// </summary>
public class MaskingLayoutRendererTests
{
    [Theory]
    [InlineData("Server=srv;Database=db;User Id=sa;Password=SuperSecret;",
                "Server=srv;Database=db;User Id=sa;Password=***;")]
    [InlineData("PWD=abc123;Foo=bar", "PWD=***;Foo=bar")]
    [InlineData("password=secret&user=lars", "password=***&user=lars")]
    [InlineData("token=eyJhbGciOi123 done", "token=*** done")]
    [InlineData("api_key=hunter2", "api_key=***")]
    [InlineData("apikey=hunter2", "apikey=***")]
    [InlineData("Bearer eyJabc.def-ghi_jkl", "Bearer ***")]
    [InlineData("Authorization: Basic dXNlcjpwYXNz", "Authorization: ***")]
    public void Mask_ReplacesSecretValues(string input, string expected)
    {
        MaskingLayoutRenderer.Mask(input).Should().Be(expected);
    }

    [Fact]
    public void Mask_LeavesNonSecretsUntouched()
    {
        MaskingLayoutRenderer.Mask("Verbindung ok fuer Server=DBSRV01 Datenbank=FOC")
            .Should().Be("Verbindung ok fuer Server=DBSRV01 Datenbank=FOC");
    }

    [Fact]
    public void Mask_HandlesEmptyAndNull()
    {
        MaskingLayoutRenderer.Mask(null).Should().Be(string.Empty);
        MaskingLayoutRenderer.Mask("").Should().Be(string.Empty);
    }

    [Fact]
    public void Mask_MultipleSecretsInSameLine()
    {
        MaskingLayoutRenderer.Mask("Password=abc; Server=x; Authorization: Bearer xyz")
            .Should().Be("Password=***; Server=x; Authorization: ***");
    }
}
