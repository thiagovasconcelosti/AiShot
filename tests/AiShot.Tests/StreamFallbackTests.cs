using System.Runtime.CompilerServices;
using AiShot.Ai;

namespace AiShot.Tests;

/// <summary>
/// Fallback do streaming: o que acontece com o texto já entregue quando o
/// provedor principal falha.
/// </summary>
public class StreamFallbackTests
{
    /// <summary>Provedor de mentira: emite pedaços e, opcionalmente, falha no meio.</summary>
    private sealed class ProviderFalso : IAiProvider
    {
        private readonly string[] _pedacos;
        private readonly Exception? _falhaApos;
        private readonly int _pedacosAntesDaFalha;

        public ProviderFalso(string nome, string[] pedacos, Exception? falhaApos = null, int pedacosAntesDaFalha = 0)
        {
            Name = nome;
            _pedacos = pedacos;
            _falhaApos = falhaApos;
            _pedacosAntesDaFalha = pedacosAntesDaFalha;
        }

        public string Name { get; }

        public Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AiResponse(string.Concat(_pedacos), Name, "modelo"));

        public async IAsyncEnumerable<string> StreamAsync(
            AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (int i = 0; i < _pedacos.Length; i++)
            {
                if (_falhaApos is not null && i == _pedacosAntesDaFalha) throw _falhaApos;
                yield return _pedacos[i];
                await Task.Yield();
            }
            if (_falhaApos is not null && _pedacosAntesDaFalha >= _pedacos.Length) throw _falhaApos;
        }
    }

    private static readonly AiRequest Pedido = new("pergunta");

    private static Task<string> Rodar(
        IAiProvider principal, IAiProvider? fallback, List<string> recebidos) =>
        AiService.StreamComFallbackAsync(
            () => principal,
            fallback is null ? null : () => fallback,
            Pedido,
            recebidos.Add,
            CancellationToken.None);

    // ---------- Caminho feliz ----------

    [Fact]
    public async Task ComPrincipalOk_EntregaOTextoAcumuladoACadaPedaco()
    {
        var recebidos = new List<string>();
        var principal = new ProviderFalso("principal", ["Olá", ", ", "mundo"]);

        var texto = await Rodar(principal, null, recebidos);

        Assert.Equal("Olá, mundo", texto);
        Assert.Equal(["Olá", "Olá, ", "Olá, mundo"], recebidos);
    }

    [Fact]
    public async Task ComPrincipalOk_NaoChamaOFallback()
    {
        var recebidos = new List<string>();
        var principal = new ProviderFalso("principal", ["ok"]);
        var fallback = new ProviderFalso("fallback", ["nunca"]);

        var texto = await Rodar(principal, fallback, recebidos);

        Assert.Equal("ok", texto);
        Assert.DoesNotContain("nunca", texto, StringComparison.Ordinal);
    }

    // ---------- Fallback ----------

    [Fact]
    public async Task ComFalhaNoMeioDoFluxo_DescartaOParcialAntesDeTentarOFallback()
    {
        // O ponto do teste: o consumidor precisa receber a string vazia entre as
        // duas respostas. Sem isso, o começo da primeira ficaria desenhado na
        // tela e a segunda seria emendada nele.
        var recebidos = new List<string>();
        var principal = new ProviderFalso(
            "principal", ["metade da ", "resposta"],
            falhaApos: new HttpRequestException("caiu"), pedacosAntesDaFalha: 1);
        var fallback = new ProviderFalso("fallback", ["resposta ", "inteira"]);

        var texto = await Rodar(principal, fallback, recebidos);

        Assert.Equal("resposta inteira", texto);
        Assert.Contains("", recebidos.Where(r => r.Length == 0));
        int descarte = recebidos.IndexOf("");
        Assert.True(descarte > 0, "o descarte precisa vir depois do parcial do principal");
        Assert.All(recebidos.Take(descarte), r => Assert.StartsWith("metade da", r, StringComparison.Ordinal));
        Assert.All(recebidos.Skip(descarte + 1), r => Assert.StartsWith("resposta", r, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ComFalhaNoMeioDoFluxo_NaoMisturaOTextoDosDoisProvedores()
    {
        var recebidos = new List<string>();
        var principal = new ProviderFalso(
            "principal", ["AAA", "BBB"],
            falhaApos: new HttpRequestException("caiu"), pedacosAntesDaFalha: 1);
        var fallback = new ProviderFalso("fallback", ["XXX", "YYY"]);

        var texto = await Rodar(principal, fallback, recebidos);

        Assert.Equal("XXXYYY", texto);
        Assert.DoesNotContain("AAA", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComFalhaAntesDoPrimeiroPedaco_UsaOFallback()
    {
        var recebidos = new List<string>();
        var principal = new ProviderFalso(
            "principal", ["nada"],
            falhaApos: new HttpRequestException("caiu"), pedacosAntesDaFalha: 0);
        var fallback = new ProviderFalso("fallback", ["do fallback"]);

        Assert.Equal("do fallback", await Rodar(principal, fallback, recebidos));
    }

    // ---------- Falhas terminais ----------

    [Fact]
    public async Task SemFallbackConfigurado_PropagaAFalhaDoPrincipal()
    {
        var principal = new ProviderFalso(
            "principal", ["x"], falhaApos: new HttpRequestException("motivo do principal"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Rodar(principal, null, []));

        Assert.Contains("motivo do principal", ex.Message, StringComparison.Ordinal);
        Assert.Contains("não há fallback", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComOsDoisFalhando_RelataAsDuasCausas()
    {
        var principal = new ProviderFalso("principal", ["x"], falhaApos: new HttpRequestException("erro A"));
        var fallback = new ProviderFalso("fallback", ["y"], falhaApos: new HttpRequestException("erro B"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Rodar(principal, fallback, []));

        Assert.Contains("erro A", ex.Message, StringComparison.Ordinal);
        Assert.Contains("erro B", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComCancelamento_NaoTentaOFallback()
    {
        // Fechar o overlay não é uma falha do provedor: cair no fallback aqui
        // faria uma segunda chamada de rede depois de o usuário desistir.
        var recebidos = new List<string>();
        var principal = new ProviderFalso(
            "principal", ["x"], falhaApos: new OperationCanceledException());
        var fallback = new ProviderFalso("fallback", ["não deveria rodar"]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Rodar(principal, fallback, recebidos));

        Assert.Empty(recebidos);
    }
}
