using AiShot;

namespace AiShot.Tests;

/// <summary>
/// Mascaramento de credenciais no corpo de erro antes de ele chegar à interface.
/// Alguns provedores ecoam cabeçalhos ou trechos do pedido em respostas de erro,
/// e esse texto vai parar na janela de chat e nas caixas de mensagem.
/// </summary>
public class HttpUtilSanitizeTests
{
    private const string ChaveOpenAi = "sk-proj-AbCdEf0123456789GhIjKlMnOpQrStUvWxYz";
    private const string ChaveAnthropic = "sk-ant-api03-AbCdEf0123456789GhIjKlMnOpQrStUv";

    // ---------- Chaves com prefixo ----------

    [Fact]
    public void Sanitize_MascaraChaveDaOpenAi()
    {
        var resultado = HttpUtil.Sanitize($"Incorrect API key provided: {ChaveOpenAi}");

        Assert.DoesNotContain(ChaveOpenAi, resultado, StringComparison.Ordinal);
        Assert.Contains("***", resultado, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_MascaraChaveDaAnthropic()
    {
        var resultado = HttpUtil.Sanitize($"invalid x-api-key: {ChaveAnthropic}");

        Assert.DoesNotContain(ChaveAnthropic, resultado, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_MascaraVariasChavesNaMesmaMensagem()
    {
        var resultado = HttpUtil.Sanitize($"tentou {ChaveOpenAi} e depois {ChaveAnthropic}");

        Assert.DoesNotContain(ChaveOpenAi, resultado, StringComparison.Ordinal);
        Assert.DoesNotContain(ChaveAnthropic, resultado, StringComparison.Ordinal);
    }

    // ---------- Credencial de portador ----------

    [Fact]
    public void Sanitize_MascaraCredencialDePortador()
    {
        const string token = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0";

        var resultado = HttpUtil.Sanitize($"Authorization: Bearer {token}");

        Assert.DoesNotContain(token, resultado, StringComparison.Ordinal);
        Assert.Contains("Bearer ***", resultado, StringComparison.Ordinal);
    }

    // ---------- Campos JSON ----------

    [Theory]
    [InlineData("api_key")]
    [InlineData("apiKey")]
    [InlineData("x-api-key")]
    [InlineData("authorization")]
    [InlineData("access_token")]
    [InlineData("refresh_token")]
    [InlineData("secret")]
    [InlineData("password")]
    public void Sanitize_MascaraOValorDeCamposDeSegredo(string campo)
    {
        const string valor = "valor-secreto-em-formato-proprio";

        var resultado = HttpUtil.Sanitize($$"""{"erro":"falhou","{{campo}}":"{{valor}}"}""");

        Assert.DoesNotContain(valor, resultado, StringComparison.Ordinal);
        Assert.Contains(campo, resultado, StringComparison.Ordinal);  // a chave permanece
        Assert.Contains("falhou", resultado, StringComparison.Ordinal); // o diagnóstico permanece
    }

    [Fact]
    public void Sanitize_PreservaAEstruturaDoJson()
    {
        var resultado = HttpUtil.Sanitize("""{"api_key":"segredo","modelo":"gpt-4o"}""");

        Assert.Equal("""{"api_key":"***","modelo":"gpt-4o"}""", resultado);
    }

    [Fact]
    public void Sanitize_NaoConfundeCampoDeSegredoComOutroDeNomeParecido()
    {
        const string corpo = """{"api_key_id":"visivel-123"}""";

        // "api_key_id" não é "api_key" — o valor não deve ser mascarado.
        Assert.Contains("visivel-123", HttpUtil.Sanitize(corpo), StringComparison.Ordinal);
    }

    // ---------- Texto sem segredo ----------

    [Fact]
    public void Sanitize_NaoAlteraMensagemSemCredencial()
    {
        const string corpo = """{"error":{"message":"model not found","type":"invalid_request_error"}}""";

        Assert.Equal(corpo, HttpUtil.Sanitize(corpo));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Sanitize_ComEntradaVazia_DevolveVazio(string? entrada) =>
        Assert.Equal("", HttpUtil.Sanitize(entrada));

    [Fact]
    public void Sanitize_NaoMascaraTextoCurtoComPrefixoSemelhante()
    {
        // "sk-" sozinho ou seguido de pouca coisa não é uma chave.
        Assert.Contains("sk-abc", HttpUtil.Sanitize("prefixo sk-abc no meio"), StringComparison.Ordinal);
    }

    // ---------- Integração com Truncate ----------

    [Fact]
    public void Truncate_MascaraAntesDeCortar()
    {
        // A chave fica além do limite de corte: se o mascaramento rodasse depois,
        // o pedaço visível dela escaparia junto com o texto truncado.
        var corpo = new string('x', 380) + " " + ChaveOpenAi;

        var resultado = HttpUtil.Truncate(corpo);

        Assert.DoesNotContain("sk-proj-", resultado, StringComparison.Ordinal);
    }

    [Fact]
    public void Truncate_ComChaveAntesDoLimite_TambemMascara()
    {
        var resultado = HttpUtil.Truncate($"erro com {ChaveOpenAi} no início");

        Assert.DoesNotContain(ChaveOpenAi, resultado, StringComparison.Ordinal);
        Assert.Contains("erro com", resultado, StringComparison.Ordinal);
    }

    [Fact]
    public void Truncate_ContinuaRespeitandoOLimiteDepoisDeMascarar()
    {
        var resultado = HttpUtil.Truncate(new string('x', 1000), max: 50);

        Assert.Equal(51, resultado.Length); // 50 caracteres + reticência
    }
}
