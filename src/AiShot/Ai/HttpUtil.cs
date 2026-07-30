using System.Text.RegularExpressions;

namespace AiShot;

/// <summary>Utilitários compartilhados das chamadas HTTP.</summary>
internal static partial class HttpUtil
{
    /// <summary>
    /// Higieniza o corpo de erro antes de expor em exceção/UI: mascara segredos
    /// e depois trunca respostas longas.
    /// </summary>
    /// <remarks>
    /// A ordem importa. Mascarar depois de truncar deixaria passar uma chave
    /// cortada ao meio pela borda do corte — ainda um vazamento parcial, e ainda
    /// suficiente para identificar a credencial.
    /// </remarks>
    public static string Truncate(string? body, int max = 400)
    {
        if (string.IsNullOrEmpty(body)) return "";
        body = Sanitize(body).Trim();
        return body.Length <= max ? body : body[..max] + "…";
    }

    /// <summary>
    /// Substitui credenciais reconhecíveis por um marcador. Alguns provedores
    /// ecoam cabeçalhos ou trechos do pedido na resposta de erro, e esse texto
    /// chega à janela de chat e às caixas de mensagem.
    /// </summary>
    /// <remarks>
    /// Cobre o que é reconhecível por forma: chaves no padrão da OpenAI e da
    /// Anthropic (<c>sk-…</c>, <c>sk-ant-…</c>), credenciais de portador e
    /// campos JSON de nome conhecido. Não é uma garantia — um provedor que ecoe
    /// a chave num formato próprio passa despercebido. É uma rede de contenção
    /// sobre um vazamento que não deveria acontecer, não a defesa principal.
    /// </remarks>
    public static string Sanitize(string? body)
    {
        if (string.IsNullOrEmpty(body)) return "";

        body = ChavesComPrefixo().Replace(body, "***");
        body = Portador().Replace(body, "Bearer ***");
        body = CamposDeSegredo().Replace(body, "$1***$2");
        return body;
    }

    // "sk-" seguido de pelo menos 16 caracteres de chave. Cobre tanto o formato
    // da OpenAI quanto o da Anthropic ("sk-ant-…"), que compartilham o prefixo.
    [GeneratedRegex(@"sk-[A-Za-z0-9\-_]{16,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ChavesComPrefixo();

    // Credencial de portador em cabeçalho ecoado.
    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]{8,}=*", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Portador();

    // Campos JSON cujo nome indica segredo, qualquer que seja o formato do valor.
    // Os grupos preservam a chave e o fecho das aspas; só o miolo é trocado.
    [GeneratedRegex(
        """("(?:api[_-]?key|x-api-key|authorization|access[_-]?token|refresh[_-]?token|secret|password)"\s*:\s*")[^"]*(")""",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CamposDeSegredo();

    /// <summary>
    /// Cria um CTS com timeout por-operação, encadeado ao token do chamador. Cada
    /// chamada HTTP define seu próprio limite (o HttpClient não tem timeout global fixo).
    /// </summary>
    public static CancellationTokenSource Timeout(CancellationToken ct, TimeSpan timeout)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return cts;
    }
}
