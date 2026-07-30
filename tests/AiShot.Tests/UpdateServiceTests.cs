using AiShot.App;

namespace AiShot.Tests;

/// <summary>
/// Validação da origem do instalador baixado pelo auto-update.
/// É a barreira que impede o aplicativo de executar um binário vindo de um host
/// inesperado, então os casos negativos importam tanto quanto os positivos.
/// </summary>
public class UpdateServiceTests
{
    // ---------- URLs aceitas ----------

    [Theory]
    [InlineData("https://github.com/thiagovasconcelosti/AiShot/releases/download/v0.1.3/AiShot-Setup-0.1.3.exe")]
    [InlineData("https://objects.githubusercontent.com/algum/caminho/AiShot-Setup.exe")]
    [InlineData("https://api.github.com/repos/x/y/releases/assets/1")]
    [InlineData("https://GITHUB.COM/x/y")]                 // host não diferencia caixa
    [InlineData("https://github.com:443/x/y")]             // porta padrão explícita
    public void IsTrustedUrl_AceitaHostsDoGitHubSobreHttps(string url) =>
        Assert.True(UpdateService.IsTrustedUrl(url));

    // ---------- Esquema ----------

    [Theory]
    [InlineData("http://github.com/x/y")]                  // sem TLS
    [InlineData("ftp://github.com/x/y")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    public void IsTrustedUrl_RecusaEsquemasQueNaoSaoHttps(string url) =>
        Assert.False(UpdateService.IsTrustedUrl(url));

    // ---------- Hosts semelhantes ----------

    [Theory]
    [InlineData("https://github.com.invasor.example/x")]   // github.com como prefixo do domínio
    [InlineData("https://notgithub.com/x")]
    [InlineData("https://githubbcom/x")]
    [InlineData("https://github.io/x")]
    [InlineData("https://raw.githubusercontent.com.invasor.example/x")]
    [InlineData("https://example.com/AiShot-Setup.exe")]
    public void IsTrustedUrl_RecusaHostsApenasParecidos(string url) =>
        Assert.False(UpdateService.IsTrustedUrl(url));

    [Fact]
    public void IsTrustedUrl_RecusaCredenciaisQueDisfarcamOHost()
    {
        // O host real é "invasor.example"; "github.com" aqui é só o usuário.
        Assert.False(UpdateService.IsTrustedUrl("https://github.com@invasor.example/x"));
    }

    // ---------- Entradas malformadas ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("não é uma url")]
    [InlineData("/releases/download/AiShot-Setup.exe")]    // relativa, sem host
    [InlineData("github.com/x/y")]                         // sem esquema
    public void IsTrustedUrl_RecusaEntradasMalformadas(string? url) =>
        Assert.False(UpdateService.IsTrustedUrl(url));

    // ---------- Versão atual ----------

    [Fact]
    public void Current_DevolveUmaVersaoUtilizavel()
    {
        var v = UpdateService.Current;

        Assert.True(v.Major >= 0);
        Assert.True(v.Build >= 0, "O componente de build nunca deve ser negativo.");
        Assert.Equal(-1, v.Revision); // construída com três componentes
    }

    // ---------- Leitura do arquivo de checksum ----------

    private const string HashValido = "116B689374E078C96BE186A1790794A588A4E6E12C3B177F35CB095F58223122";

    [Fact]
    public void ExtrairHash_NoFormatoDoSha256sum_DevolveApenasOHash()
    {
        // É o formato que o fluxo de release grava: "<hash>  <nome do arquivo>".
        Assert.Equal(HashValido, UpdateService.ExtrairHash($"{HashValido}  AiShot-Setup-0.1.3.exe"));
    }

    [Fact]
    public void ExtrairHash_ComApenasOHash_Aceita() =>
        Assert.Equal(HashValido, UpdateService.ExtrairHash(HashValido));

    [Fact]
    public void ExtrairHash_ComQuebraDeLinhaAoFinal_Aceita() =>
        Assert.Equal(HashValido, UpdateService.ExtrairHash($"{HashValido}  arquivo.exe\n"));

    [Fact]
    public void ExtrairHash_ComVariasLinhas_UsaAPrimeira() =>
        Assert.Equal(HashValido, UpdateService.ExtrairHash($"{HashValido}  a.exe\noutra-linha-qualquer\n"));

    [Fact]
    public void ExtrairHash_ComTabulacaoComoSeparador_Aceita() =>
        Assert.Equal(HashValido, UpdateService.ExtrairHash($"{HashValido}\tarquivo.exe"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("não é um hash")]
    [InlineData("116B689374E078C96BE186A1790794A588A4E6E12C3B177F35CB095F5822312")]    // 63 dígitos
    [InlineData("116B689374E078C96BE186A1790794A588A4E6E12C3B177F35CB095F582231222")]  // 65 dígitos
    [InlineData("116B689374E078C96BE186A1790794A588A4E6E12C3B177F35CB095F5822312Z")]   // caractere inválido
    [InlineData("<!DOCTYPE html><html>página de erro</html>")]
    public void ExtrairHash_ComConteudoInvalido_DevolveNulo(string? conteudo) =>
        Assert.Null(UpdateService.ExtrairHash(conteudo));

    [Fact]
    public void ExtrairHash_NaoDiferenciaMaiusculasDeMinusculas()
    {
        // A comparação de integridade é feita sem diferenciar caixa; a leitura
        // apenas precisa aceitar as duas formas.
        Assert.NotNull(UpdateService.ExtrairHash(HashValido.ToLowerInvariant()));
        Assert.NotNull(UpdateService.ExtrairHash(HashValido.ToUpperInvariant()));
    }
}
