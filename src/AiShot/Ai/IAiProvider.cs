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
}

/// <summary>
/// Orquestra visão → principal → fallback.
/// Se a IA de visão estiver ativa, descreve a imagem primeiro; a descrição é
/// injetada no contexto da IA principal antes de responder a pergunta do usuário.
/// </summary>
public interface IAiService
{
    /// <summary>
    /// Pergunta algo sobre a imagem do print.
    /// </summary>
    /// <param name="question">Pergunta do usuário.</param>
    /// <param name="imagePng">PNG da área selecionada.</param>
    Task<AiResponse> AskAboutImageAsync(string question, byte[] imagePng, CancellationToken ct = default);

    /// <summary>Cria uma conversa contínua sobre a imagem informada.</summary>
    IAiChatSession CreateSession(byte[] imagePng);
}
