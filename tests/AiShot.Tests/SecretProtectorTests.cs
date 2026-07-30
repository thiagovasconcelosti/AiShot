using System.Text;
using AiShot.Config;

namespace AiShot.Tests;

/// <summary>
/// Cifra de segredos em repouso via DPAPI no escopo do usuário atual.
/// Os testes rodam contra a API real do Windows — não há dublê, porque o valor
/// do componente está justamente em produzir algo que só o usuário atual lê.
/// </summary>
public class SecretProtectorTests
{
    private const string Prefixo = "enc:";

    // ---------- Ida e volta ----------

    [Theory]
    [InlineData("sk-ant-api03-exemplo")]
    [InlineData("a")]
    [InlineData("com espaços e acentuação: ção")]
    [InlineData("emoji 🔐 e símbolos ⚠")]
    public void Protect_SeguidoDeUnprotect_DevolveOValorOriginal(string original)
    {
        var recuperado = SecretProtector.Unprotect(SecretProtector.Protect(original));

        Assert.Equal(original, recuperado);
    }

    [Fact]
    public void Protect_ComTextoLongo_SobreviveAIdaEVolta()
    {
        var original = new string('x', 8192);

        Assert.Equal(original, SecretProtector.Unprotect(SecretProtector.Protect(original)));
    }

    [Fact]
    public void Protect_NaoDeixaOTextoOriginalVisivelNoResultado()
    {
        const string original = "sk-ant-api03-segredo";

        var cifrado = SecretProtector.Protect(original);

        Assert.DoesNotContain(original, cifrado, StringComparison.Ordinal);
    }

    [Fact]
    public void Protect_MarcaOResultadoComOPrefixo()
    {
        Assert.StartsWith(Prefixo, SecretProtector.Protect("qualquer-coisa"), StringComparison.Ordinal);
    }

    // ---------- Entradas vazias ----------

    [Fact]
    public void Protect_ComNulo_DevolveVazio() =>
        Assert.Equal("", SecretProtector.Protect(null));

    [Fact]
    public void Protect_ComVazio_DevolveVazio() =>
        Assert.Equal("", SecretProtector.Protect(""));

    [Fact]
    public void Unprotect_ComNulo_DevolveVazio() =>
        Assert.Equal("", SecretProtector.Unprotect(null));

    [Fact]
    public void Unprotect_ComVazio_DevolveVazio() =>
        Assert.Equal("", SecretProtector.Unprotect(""));

    // ---------- Idempotência ----------

    [Fact]
    public void Protect_SobreValorJaCifrado_NaoCifraDeNovo()
    {
        var umaVez = SecretProtector.Protect("segredo");

        var duasVezes = SecretProtector.Protect(umaVez);

        Assert.Equal(umaVez, duasVezes);
        // E continua legível: uma dupla cifra teria quebrado a recuperação.
        Assert.Equal("segredo", SecretProtector.Unprotect(duasVezes));
    }

    // ---------- Valores em texto puro (configurações legadas) ----------

    [Fact]
    public void Unprotect_ComTextoPuroSemPrefixo_DevolveOValorInalterado()
    {
        // Configurações anteriores à cifra guardavam a chave em claro; elas
        // precisam continuar carregando até o próximo Save regravá-las.
        Assert.Equal("chave-em-claro", SecretProtector.Unprotect("chave-em-claro"));
    }

    [Fact]
    public void IsProtected_DistingueValorCifradoDeTextoPuro()
    {
        Assert.True(SecretProtector.IsProtected(SecretProtector.Protect("x")));
        Assert.False(SecretProtector.IsProtected("x"));
        Assert.False(SecretProtector.IsProtected(""));
        Assert.False(SecretProtector.IsProtected(null));
    }

    // ---------- Entradas corrompidas ----------

    [Fact]
    public void Unprotect_ComBase64Invalido_DevolveVazio()
    {
        // Não deve lançar: um arquivo corrompido tem de degradar para "sem chave",
        // e não derrubar a inicialização do aplicativo.
        Assert.Equal("", SecretProtector.Unprotect(Prefixo + "isto-não-é-base64!!!"));
    }

    [Fact]
    public void Unprotect_ComBase64ValidoMasNaoCifrado_DevolveVazio()
    {
        var base64Qualquer = Convert.ToBase64String(Encoding.UTF8.GetBytes("dados quaisquer"));

        Assert.Equal("", SecretProtector.Unprotect(Prefixo + base64Qualquer));
    }

    [Fact]
    public void Unprotect_ComPrefixoESemConteudo_DevolveVazio() =>
        Assert.Equal("", SecretProtector.Unprotect(Prefixo));

    [Fact]
    public void Unprotect_ComPayloadAdulterado_DevolveVazio()
    {
        var cifrado = SecretProtector.Protect("segredo");
        var corpo = Convert.FromBase64String(cifrado[Prefixo.Length..]);
        corpo[^1] ^= 0xFF; // vira os bits do último byte

        var adulterado = Prefixo + Convert.ToBase64String(corpo);

        Assert.Equal("", SecretProtector.Unprotect(adulterado));
    }
}
