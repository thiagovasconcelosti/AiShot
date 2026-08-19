using AiShot.App;

namespace AiShot.Tests;

public class ComplianceLinksTests
{
    [Fact]
    public void PrivacyPolicy_UsaHttpsNoDominioOficial()
    {
        Assert.Equal(Uri.UriSchemeHttps, ComplianceLinks.PrivacyPolicy.Scheme);
        Assert.Equal("aishot.tecnologia.dev.br", ComplianceLinks.PrivacyPolicy.Host);
    }

    [Fact]
    public void BuildReportUri_EnderecaSuporteSemAnexarConteudoDoUsuario()
    {
        var uri = ComplianceLinks.BuildReportUri("Resposta inadequada");

        Assert.Equal("mailto", uri.Scheme);
        Assert.StartsWith("mailto:suporte@tecnologia.dev.br?subject=", uri.OriginalString);
        Assert.DoesNotContain("body=", uri.OriginalString, StringComparison.OrdinalIgnoreCase);
    }
}
