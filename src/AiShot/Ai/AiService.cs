using System.Net.Http;
using AiShot.Config;

namespace AiShot.Ai;

/// <summary>
/// Orquestra o fluxo de IA: visão (opcional) -> IA principal -> fallback.
/// </summary>
public sealed class AiService : IAiService
{
    private readonly AppConfig _cfg;
    private readonly HttpClient _http;

    public AiService(AppConfig cfg, HttpClient http)
    {
        _cfg = cfg;
        _http = http;
    }

    public IAiChatSession CreateSession(byte[] imagePng) => new ChatSession(this, imagePng);

    // ---------- Helpers compartilhados ----------

    // Limites de saída por chamada.
    //
    // Precisam de folga além do tamanho da resposta esperada: em modelos com
    // raciocínio, o limite cobre o raciocínio E o texto final, então um valor
    // ajustado à resposta faz o modelo gastar a cota pensando e devolver um
    // texto cortado ao meio. A folga custa nada quando não é usada — a cobrança
    // é pelos tokens gerados, não pelo teto.
    private const int TokensDaDescricaoDeImagem = 4096;
    private const int TokensDaResposta = 8192;

    /// <summary>Roda a IA de visão (se ativa) e devolve a descrição da imagem, ou null.</summary>
    private async Task<string?> DescribeAsync(byte[] imagePng, CancellationToken ct)
    {
        var v = _cfg.Ai.Vision;
        if (!v.Enabled || string.IsNullOrWhiteSpace(v.ApiKey)) return null;

        var provider = AiProviderFactory.Create(v.Provider, v.ApiKey, v.Model, v.BaseUrl, _http);
        var req = new AiRequest(
            "Descreva objetivamente o conteúdo desta imagem em detalhes (texto, elementos visuais, cores, layout).",
            imagePng: imagePng,
            maxTokens: TokensDaDescricaoDeImagem);
        var resp = await provider.CompleteAsync(req, ct).ConfigureAwait(false);
        return resp.Text;
    }

    private async Task<AiResponse> CompleteWithFallbackAsync(AiRequest req, CancellationToken ct)
    {
        var ai = _cfg.Ai;
        try
        {
            var main = AiProviderFactory.Create(ai.Provider, ai.ApiKey, ai.Model, ai.BaseUrl, _http);
            return await main.CompleteAsync(req, ct).ConfigureAwait(false);
        }
        catch (Exception mainEx)
        {
            var fb = ai.Fallback;
            if (fb is not null && !string.IsNullOrWhiteSpace(fb.ApiKey))
            {
                try
                {
                    var fbProvider = AiProviderFactory.Create(fb.Provider, fb.ApiKey, fb.Model, fb.BaseUrl, _http);
                    return await fbProvider.CompleteAsync(req, ct).ConfigureAwait(false);
                }
                catch (Exception fbEx)
                {
                    throw new InvalidOperationException(
                        $"IA principal e fallback falharam. Principal: {mainEx.Message} | Fallback: {fbEx.Message}", fbEx);
                }
            }
            throw new InvalidOperationException(
                $"IA principal falhou e não há fallback configurado. {mainEx.Message}", mainEx);
        }
    }

    /// <summary>
    /// Como <see cref="CompleteWithFallbackAsync"/>, mas entregando o texto em
    /// pedaços. Devolve o texto completo ao final.
    /// </summary>
    /// <remarks>
    /// Uma falha no meio do fluxo deixa o texto pela metade. Antes de tentar o
    /// fallback, o parcial é descartado (<paramref name="aoReceber"/> recebe
    /// string vazia) e o segundo provedor recomeça do zero: emendar o começo de
    /// uma resposta no fim de outra produziria um texto que nenhum dos dois
    /// escreveu.
    /// </remarks>
    private Task<string> StreamWithFallbackAsync(
        AiRequest req, Action<string> aoReceber, CancellationToken ct)
    {
        var ai = _cfg.Ai;
        var fb = ai.Fallback;

        return StreamComFallbackAsync(
            () => AiProviderFactory.Create(ai.Provider, ai.ApiKey, ai.Model, ai.BaseUrl, _http),
            fb is not null && !string.IsNullOrWhiteSpace(fb.ApiKey)
                ? () => AiProviderFactory.Create(fb.Provider, fb.ApiKey, fb.Model, fb.BaseUrl, _http)
                : null,
            req, aoReceber, ct);
    }

    /// <summary>
    /// Núcleo do fallback com streaming, independente da configuração — os
    /// providers chegam prontos para que o comportamento possa ser verificado
    /// sem rede.
    /// </summary>
    internal static async Task<string> StreamComFallbackAsync(
        Func<IAiProvider> principal,
        Func<IAiProvider>? fallback,
        AiRequest req,
        Action<string> aoReceber,
        CancellationToken ct)
    {
        try
        {
            return await AcumularAsync(principal(), req, aoReceber, ct).ConfigureAwait(false);
        }
        catch (Exception mainEx) when (mainEx is not OperationCanceledException)
        {
            if (fallback is not null)
            {
                aoReceber(""); // descarta o parcial do principal
                try
                {
                    return await AcumularAsync(fallback(), req, aoReceber, ct).ConfigureAwait(false);
                }
                catch (Exception fbEx) when (fbEx is not OperationCanceledException)
                {
                    throw new InvalidOperationException(
                        $"IA principal e fallback falharam. Principal: {mainEx.Message} | Fallback: {fbEx.Message}", fbEx);
                }
            }
            throw new InvalidOperationException(
                $"IA principal falhou e não há fallback configurado. {mainEx.Message}", mainEx);
        }
    }

    /// <summary>Consome o fluxo do provedor, notificando o texto acumulado a cada pedaço.</summary>
    private static async Task<string> AcumularAsync(
        IAiProvider provider, AiRequest req, Action<string> aoReceber, CancellationToken ct)
    {
        var texto = new System.Text.StringBuilder();
        await foreach (var pedaco in provider.StreamAsync(req, ct).ConfigureAwait(false))
        {
            texto.Append(pedaco);
            aoReceber(texto.ToString());
        }
        return texto.ToString();
    }

    // ---------- Conversa contínua ----------

    private sealed class ChatSession : IAiChatSession
    {
        private readonly AiService _owner;
        private readonly byte[] _image;
        private readonly List<ChatMessage> _history = new();
        private string? _visionDescription;
        private bool _visionDone;

        public ChatSession(AiService owner, byte[] image)
        {
            _owner = owner;
            _image = image;
        }

        public IReadOnlyList<ChatMessage> History => _history;

        public async Task<string> SendAsync(string userMessage, CancellationToken ct = default)
        {
            var req = await PrepararTurnoAsync(userMessage, ct).ConfigureAwait(false);
            var resp = await _owner.CompleteWithFallbackAsync(req, ct).ConfigureAwait(false);

            _history.Add(new ChatMessage("assistant", resp.Text));
            return resp.Text;
        }

        public async Task<string> SendStreamingAsync(
            string userMessage, Action<string> aoReceber, CancellationToken ct = default)
        {
            var req = await PrepararTurnoAsync(userMessage, ct).ConfigureAwait(false);
            var texto = await _owner.StreamWithFallbackAsync(req, aoReceber, ct).ConfigureAwait(false);

            _history.Add(new ChatMessage("assistant", texto));
            return texto;
        }

        /// <summary>
        /// Registra o turno do usuário, roda a visão na primeira mensagem e monta
        /// a requisição com o histórico completo.
        /// </summary>
        private async Task<AiRequest> PrepararTurnoAsync(string userMessage, CancellationToken ct)
        {
            // Sem visão: a imagem viaja no 1º turno de user; como enviamos o histórico
            // inteiro a cada chamada, a API preserva o contexto visual nos turnos
            // seguintes (sem reenviar a imagem). O turno é adicionado antes da visão
            // para o chat já poder exibi-lo enquanto a resposta não chega.
            byte[]? turnImage = _history.Count == 0 ? _image : null;
            _history.Add(new ChatMessage("user", userMessage, turnImage));

            // Visão roda uma única vez, na primeira mensagem.
            if (!_visionDone)
            {
                _visionDescription = await _owner.DescribeAsync(_image, ct).ConfigureAwait(false);
                _visionDone = true;
            }

            string? systemPrompt = null;
            IReadOnlyList<ChatMessage> messages = _history;
            if (_visionDescription is not null)
            {
                systemPrompt = $"Contexto visual da imagem (fornecido por IA de visão): {_visionDescription}";
                // Com visão ativa, não reenvia a imagem: o contexto já está no system.
                messages = _history.Select(m => m.ImagePng is null ? m : m with { ImagePng = null }).ToArray();
            }

            return new AiRequest(messages, systemPrompt, MaxTokens: TokensDaResposta);
        }
    }
}
