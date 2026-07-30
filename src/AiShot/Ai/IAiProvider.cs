namespace AiShot.Ai;

/// <summary>Uma mensagem da conversa ("user" | "assistant") com imagem opcional.</summary>
public sealed record ChatMessage(string Role, string Text, byte[]? ImagePng = null);

/// <summary>
/// Requisição de chat multimodal: lista de mensagens nativa (role+conteúdo) que
/// os providers traduzem para o messages[] real da API. O histórico NÃO é achatado
/// em string — cada turno vira uma entrada com seu papel.
/// </summary>
public sealed record AiRequest(
    IReadOnlyList<ChatMessage> Messages,
    string? SystemPrompt = null,
    int MaxTokens = 1024)
{
    /// <summary>Conveniência: requisição de turno único (texto + imagem opcional).</summary>
    public AiRequest(string prompt, byte[]? imagePng = null, string? systemPrompt = null, int maxTokens = 1024)
        : this(new[] { new ChatMessage("user", prompt, imagePng) }, systemPrompt, maxTokens) { }
}

/// <summary>Resposta da IA.</summary>
public sealed record AiResponse(string Text, string ProviderUsed, string Model);

/// <summary>
/// Provedor de IA (Anthropic ou OpenAI). Implementações em Ai/Providers/.
/// </summary>
public interface IAiProvider
{
    /// <summary>Nome do provedor ("anthropic" | "openai").</summary>
    string Name { get; }

    /// <summary>Envia prompt (com imagem opcional) e retorna texto.</summary>
    Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default);

    /// <summary>
    /// Envia o prompt e devolve o texto em pedaços, conforme a API os produz.
    /// </summary>
    /// <remarks>
    /// Cada item é um incremento, não o texto acumulado — cabe a quem consome
    /// concatenar. Uma falha no meio da enumeração deixa o texto pela metade, e
    /// o chamador precisa descartá-lo antes de tentar outro provedor: misturar
    /// duas respostas seria pior do que perder a primeira.
    /// </remarks>
    IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct = default);
}

/// <summary>
/// Conversa contínua sobre uma imagem. Mantém o histórico como lista de mensagens
/// nativas (a fonte única da verdade do diálogo). A imagem viaja no primeiro turno e
/// o contexto é preservado nos turnos seguintes; a IA de visão (se ativa) descreve a
/// imagem uma única vez e a descrição é reaproveitada via system prompt.
/// </summary>
public interface IAiChatSession
{
    IReadOnlyList<ChatMessage> History { get; }
    Task<string> SendAsync(string userMessage, CancellationToken ct = default);

    /// <summary>
    /// Envia a mensagem e devolve a resposta em pedaços, conforme a API os produz.
    /// Devolve o texto completo ao final e o registra no histórico.
    /// </summary>
    /// <param name="aoReceber">
    /// Chamado a cada incremento com o texto acumulado até ali. Pode ser chamado
    /// com string vazia — quando o provedor principal falha no meio da resposta,
    /// o parcial é descartado antes de o fallback recomeçar do zero, e quem
    /// desenha precisa saber que o texto anterior não vale mais.
    /// </param>
    Task<string> SendStreamingAsync(
        string userMessage,
        Action<string> aoReceber,
        CancellationToken ct = default);
}

/// <summary>
/// Orquestra visão → principal → fallback.
/// Se a IA de visão estiver ativa, descreve a imagem primeiro; a descrição é
/// injetada no contexto da IA principal antes de responder a pergunta do usuário.
/// </summary>
public interface IAiService
{
    /// <summary>Cria uma conversa contínua sobre a imagem informada.</summary>
    IAiChatSession CreateSession(byte[] imagePng);
}
