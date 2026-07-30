using AiShot;

namespace AiShot.Tests;

/// <summary>
/// Utilitários compartilhados das chamadas HTTP: truncamento de corpo de erro e
/// tempo limite por operação.
/// </summary>
public class HttpUtilTests
{
    // ---------- Truncate ----------

    [Fact]
    public void Truncate_ComTextoCurto_DevolveInalterado() =>
        Assert.Equal("erro breve", HttpUtil.Truncate("erro breve"));

    [Fact]
    public void Truncate_RemoveEspacosDasBordas() =>
        Assert.Equal("erro", HttpUtil.Truncate("  \n erro \t "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Truncate_ComEntradaVazia_DevolveVazio(string? entrada) =>
        Assert.Equal("", HttpUtil.Truncate(entrada));

    [Fact]
    public void Truncate_ComApenasEspacos_DevolveVazio() =>
        Assert.Equal("", HttpUtil.Truncate("   \n\t  "));

    [Fact]
    public void Truncate_NoLimiteExato_NaoCorta()
    {
        var texto = new string('x', 400);

        Assert.Equal(texto, HttpUtil.Truncate(texto));
    }

    [Fact]
    public void Truncate_AcimaDoLimite_CortaEMarcaComReticencias()
    {
        var resultado = HttpUtil.Truncate(new string('x', 401));

        Assert.Equal(401, resultado.Length); // 400 caracteres + a reticência
        Assert.EndsWith("…", resultado, StringComparison.Ordinal);
    }

    [Fact]
    public void Truncate_ComLimitePersonalizado_RespeitaOValorInformado()
    {
        var resultado = HttpUtil.Truncate(new string('x', 100), max: 10);

        Assert.Equal("xxxxxxxxxx…", resultado);
    }

    [Fact]
    public void Truncate_AparaAntesDeMedirOComprimento()
    {
        // O espaço em volta não deve consumir o orçamento de caracteres.
        var resultado = HttpUtil.Truncate("   " + new string('x', 10) + "   ", max: 10);

        Assert.Equal(new string('x', 10), resultado);
    }

    // ---------- Timeout ----------

    [Fact]
    public void Timeout_CancelaQuandoOPrazoExpira()
    {
        using var cts = HttpUtil.Timeout(CancellationToken.None, TimeSpan.FromMilliseconds(30));

        Assert.True(cts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)),
            "O token deveria ter sido cancelado dentro do prazo.");
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Timeout_PropagaOCancelamentoDoChamador()
    {
        using var doChamador = new CancellationTokenSource();
        using var cts = HttpUtil.Timeout(doChamador.Token, TimeSpan.FromHours(1));

        doChamador.Cancel();

        Assert.True(cts.IsCancellationRequested,
            "Cancelar o token de origem deve cancelar o encadeado, sem esperar o prazo.");
    }

    [Fact]
    public void Timeout_ComTokenJaCancelado_NasceCancelado()
    {
        using var doChamador = new CancellationTokenSource();
        doChamador.Cancel();

        using var cts = HttpUtil.Timeout(doChamador.Token, TimeSpan.FromHours(1));

        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Timeout_AntesDoPrazo_NaoCancela()
    {
        using var cts = HttpUtil.Timeout(CancellationToken.None, TimeSpan.FromMinutes(5));

        Assert.False(cts.IsCancellationRequested);
    }
}
