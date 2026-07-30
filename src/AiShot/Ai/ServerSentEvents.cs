using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AiShot.Ai;

/// <summary>
/// Leitura de um fluxo Server-Sent Events.
/// </summary>
/// <remarks>
/// Os dois provedores entregam o texto incremental por SSE, mudando apenas o
/// formato do JSON de cada evento. A leitura do fluxo é a mesma, então fica
/// aqui e cada provider passa só o extrator do seu formato.
/// </remarks>
internal static class ServerSentEvents
{
    /// <summary>Marca o fim do fluxo no formato da OpenAI.</summary>
    public const string Fim = "[DONE]";

    /// <summary>
    /// Devolve o conteúdo de cada linha <c>data:</c> do fluxo, na ordem.
    /// </summary>
    /// <remarks>
    /// Só as linhas <c>data:</c> interessam. As linhas <c>event:</c> repetem o
    /// campo <c>type</c> que já vem dentro do JSON, e comentários (linha
    /// iniciada por <c>:</c>) são batimentos para manter a conexão viva.
    /// </remarks>
    public static async IAsyncEnumerable<string> LerDadosAsync(
        Stream fluxo,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var leitor = new StreamReader(fluxo);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // ReadLineAsync devolve null no fim do fluxo — não usamos EndOfStream,
            // que bloqueia a thread esperando o próximo byte chegar.
            var linha = await leitor.ReadLineAsync(ct).ConfigureAwait(false);
            if (linha is null) break;
            if (!linha.StartsWith("data:", StringComparison.Ordinal)) continue;

            var dados = linha["data:".Length..].TrimStart();
            if (dados.Length == 0) continue;
            if (dados == Fim) yield break;

            yield return dados;
        }
    }

    /// <summary>
    /// Extrai o texto incremental de um evento da Anthropic.
    /// </summary>
    /// <remarks>
    /// Interessa apenas <c>content_block_delta</c> com <c>text_delta</c>. Os
    /// demais eventos (<c>message_start</c>, <c>ping</c>, <c>message_delta</c>…)
    /// carregam metadados, não texto. Um evento <c>error</c> vira exceção: o
    /// fluxo pode falhar depois de já ter entregue parte do texto, e seguir em
    /// silêncio deixaria uma resposta truncada passando por completa.
    /// </remarks>
    public static string? DeltaDaAnthropic(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;

        if (!raiz.TryGetProperty("type", out var tipo)) return null;

        switch (tipo.GetString())
        {
            case "content_block_delta":
                if (!raiz.TryGetProperty("delta", out var delta)) return null;
                if (!delta.TryGetProperty("type", out var tipoDoDelta)) return null;
                if (tipoDoDelta.GetString() != "text_delta") return null;
                return delta.TryGetProperty("text", out var texto) ? texto.GetString() : null;

            case "error":
                throw new HttpRequestException(
                    $"Anthropic interrompeu o fluxo: {HttpUtil.Truncate(MensagemDeErro(raiz))}");

            default:
                return null;
        }
    }

    /// <summary>
    /// Extrai o texto incremental de um evento da OpenAI.
    /// </summary>
    /// <remarks>
    /// O texto vem em <c>choices[0].delta.content</c>. O primeiro evento traz
    /// só o papel da mensagem, e o último traz <c>finish_reason</c> sem
    /// conteúdo — ambos aparecem sem o campo <c>content</c>.
    /// </remarks>
    public static string? DeltaDaOpenAi(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;

        if (raiz.TryGetProperty("error", out _))
            throw new HttpRequestException(
                $"OpenAI interrompeu o fluxo: {HttpUtil.Truncate(MensagemDeErro(raiz))}");

        if (!raiz.TryGetProperty("choices", out var escolhas) || escolhas.GetArrayLength() == 0) return null;
        if (!escolhas[0].TryGetProperty("delta", out var delta)) return null;
        return delta.TryGetProperty("content", out var conteudo) ? conteudo.GetString() : null;
    }

    /// <summary>Texto do erro do evento, ou o JSON inteiro se o formato for outro.</summary>
    private static string MensagemDeErro(JsonElement raiz)
    {
        if (raiz.TryGetProperty("error", out var erro) &&
            erro.TryGetProperty("message", out var msg) &&
            msg.GetString() is { Length: > 0 } texto)
        {
            return texto;
        }
        return raiz.GetRawText();
    }
}
