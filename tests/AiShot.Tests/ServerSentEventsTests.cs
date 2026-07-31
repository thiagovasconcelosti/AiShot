using System.Text;
using AiShot.Ai;

namespace AiShot.Tests;

/// <summary>
/// Leitura do fluxo SSE e extração do texto incremental de cada provedor.
/// </summary>
public class ServerSentEventsTests
{
    private static async Task<List<string>> LerAsync(string corpo)
    {
        using var fluxo = new MemoryStream(Encoding.UTF8.GetBytes(corpo));
        var lidos = new List<string>();
        await foreach (var d in ServerSentEvents.LerDadosAsync(fluxo)) lidos.Add(d);
        return lidos;
    }

    // ---------- Leitura do fluxo ----------

    [Fact]
    public async Task LerDados_DevolveOConteudoDeCadaLinhaData()
    {
        var lidos = await LerAsync("data: {\"a\":1}\n\ndata: {\"a\":2}\n\n");

        Assert.Equal(["{\"a\":1}", "{\"a\":2}"], lidos);
    }

    [Fact]
    public async Task LerDados_IgnoraLinhasEventEComentarios()
    {
        // "event:" repete o type que já vem no JSON; ":" é batimento da conexão.
        var lidos = await LerAsync(": ping\nevent: message_start\ndata: {\"a\":1}\n\n");

        Assert.Equal("{\"a\":1}", Assert.Single(lidos));
    }

    [Fact]
    public async Task LerDados_ParaNoMarcadorDeFim()
    {
        var lidos = await LerAsync("data: {\"a\":1}\n\ndata: [DONE]\n\ndata: {\"a\":2}\n\n");

        Assert.Equal(["{\"a\":1}"], lidos);
    }

    [Fact]
    public async Task LerDados_AceitaEspacoOpcionalDepoisDoDoisPontos()
    {
        var lidos = await LerAsync("data:{\"a\":1}\n\n");

        Assert.Equal("{\"a\":1}", Assert.Single(lidos));
    }

    [Fact]
    public async Task LerDados_ComFluxoVazio_NaoDevolveNada() =>
        Assert.Empty(await LerAsync(""));

    [Fact]
    public async Task LerDados_ComFluxoTruncadoNoMeio_NaoLanca()
    {
        // Conexão cortada antes do fim: o que chegou vale, o resto some.
        var lidos = await LerAsync("data: {\"a\":1}\n\ndata: {\"a\"");

        Assert.Equal(["{\"a\":1}", "{\"a\""], lidos);
    }

    [Fact]
    public async Task LerDados_HonraOCancelamento()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var fluxo = new MemoryStream(Encoding.UTF8.GetBytes("data: {\"a\":1}\n\n"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in ServerSentEvents.LerDadosAsync(fluxo, cts.Token)) { }
        });
    }

    // ---------- Formato da Anthropic ----------

    [Fact]
    public void DeltaDaAnthropic_ExtraiOTextoIncremental()
    {
        const string json = """
            {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Olá"}}
            """;

        Assert.Equal("Olá", ServerSentEvents.DeltaDaAnthropic(json));
    }

    [Theory]
    [InlineData("""{"type":"message_start","message":{"id":"msg_1"}}""")]
    [InlineData("""{"type":"ping"}""")]
    [InlineData("""{"type":"content_block_start","content_block":{"type":"text","text":""}}""")]
    [InlineData("""{"type":"content_block_stop","index":0}""")]
    [InlineData("""{"type":"message_delta","delta":{"stop_reason":"end_turn"}}""")]
    [InlineData("""{"type":"message_stop"}""")]
    public void DeltaDaAnthropic_IgnoraEventosDeMetadados(string json) =>
        Assert.Null(ServerSentEvents.DeltaDaAnthropic(json));

    [Fact]
    public void DeltaDaAnthropic_IgnoraDeltaQueNaoEDeTexto()
    {
        // Modelos com raciocínio emitem thinking_delta no mesmo evento.
        const string json = """
            {"type":"content_block_delta","delta":{"type":"thinking_delta","thinking":"..."}}
            """;

        Assert.Null(ServerSentEvents.DeltaDaAnthropic(json));
    }

    [Fact]
    public void DeltaDaAnthropic_ComEventoDeErro_Lanca()
    {
        // Um erro no meio do fluxo não pode passar em silêncio: seguir adiante
        // entregaria uma resposta truncada como se estivesse completa.
        const string json = """
            {"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}
            """;

        var ex = Assert.Throws<HttpRequestException>(() => ServerSentEvents.DeltaDaAnthropic(json));
        Assert.Contains("Overloaded", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeltaDaAnthropic_MascaraSegredoNaMensagemDeErro()
    {
        const string json = """
            {"type":"error","error":{"message":"chave sk-ant-api03-ABCDEFGHIJKLMNOPQRS inválida"}}
            """;

        var ex = Assert.Throws<HttpRequestException>(() => ServerSentEvents.DeltaDaAnthropic(json));
        Assert.DoesNotContain("sk-ant-api03", ex.Message, StringComparison.Ordinal);
    }

    // ---------- Formato da OpenAI ----------

    [Fact]
    public void DeltaDaOpenAi_ExtraiOTextoIncremental()
    {
        const string json = """
            {"choices":[{"index":0,"delta":{"content":"Olá"}}]}
            """;

        Assert.Equal("Olá", ServerSentEvents.DeltaDaOpenAi(json));
    }

    [Fact]
    public void DeltaDaOpenAi_IgnoraOEventoQueTrazSoOPapel()
    {
        const string json = """
            {"choices":[{"index":0,"delta":{"role":"assistant"},"finish_reason":null}]}
            """;

        Assert.Null(ServerSentEvents.DeltaDaOpenAi(json));
    }

    [Fact]
    public void DeltaDaOpenAi_IgnoraOEventoFinalSemConteudo()
    {
        const string json = """
            {"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}
            """;

        Assert.Null(ServerSentEvents.DeltaDaOpenAi(json));
    }

    [Fact]
    public void DeltaDaOpenAi_ComListaDeEscolhasVazia_NaoLanca()
    {
        // Alguns servidores compatíveis emitem um evento só com usage.
        const string json = """{"choices":[],"usage":{"total_tokens":10}}""";

        Assert.Null(ServerSentEvents.DeltaDaOpenAi(json));
    }

    [Fact]
    public void DeltaDaOpenAi_ComEventoDeErro_Lanca()
    {
        const string json = """
            {"error":{"message":"Rate limit reached","type":"rate_limit_error"}}
            """;

        var ex = Assert.Throws<HttpRequestException>(() => ServerSentEvents.DeltaDaOpenAi(json));
        Assert.Contains("Rate limit reached", ex.Message, StringComparison.Ordinal);
    }
}
