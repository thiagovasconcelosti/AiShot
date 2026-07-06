using System.Net.Http;
using System.Text;
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

    public async Task<AiResponse> AskAboutImageAsync(string question, byte[] imagePng, CancellationToken ct = default)
    {
        var visionDescription = await DescribeAsync(imagePng, ct).ConfigureAwait(false);
        var (systemPrompt, imageForMain) = BuildContext(visionDescription, imagePng);
        var req = new AiRequest(question, imageForMain, systemPrompt);
        return await CompleteWithFallbackAsync(req, ct).ConfigureAwait(false);
    }

    public IAiChatSession CreateSession(byte[] imagePng) => new ChatSession(this, imagePng);

    // ---------- Helpers compartilhados ----------

    /// <summary>Roda a IA de visão (se ativa) e devolve a descrição da imagem, ou null.</summary>
    private async Task<string?> DescribeAsync(byte[] imagePng, CancellationToken ct)
    {
        var v = _cfg.Ai.Vision;
        if (!v.Enabled || string.IsNullOrWhiteSpace(v.ApiKey)) return null;

        var provider = AiProviderFactory.Create(v.Provider, v.ApiKey, v.Model, v.BaseUrl, _http);
        var req = new AiRequest(
            "Descreva objetivamente o conteúdo desta imagem em detalhes (texto, elementos visuais, cores, layout).",
            imagePng: imagePng,
            maxTokens: 1024);
        var resp = await provider.CompleteAsync(req, ct).ConfigureAwait(false);
        return resp.Text;
    }

    /// <summary>
    /// Com visão: contexto vai no system, imagem omitida. Sem visão: reenvia a imagem.
    /// </summary>
    private static (string? systemPrompt, byte[]? image) BuildContext(string? visionDescription, byte[] imagePng)
    {
        if (visionDescription is not null)
            return ($"Contexto visual da imagem (fornecido por IA de visão): {visionDescription}", null);
        return (null, imagePng);
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

    // ---------- Conversa contínua ----------

    private sealed class ChatSession : IAiChatSession
    {
        private readonly AiService _owner;
        private readonly byte[] _image;
        private readonly List<ChatTurn> _history = new();
        private string? _visionDescription;
        private bool _visionDone;

        public ChatSession(AiService owner, byte[] image)
        {
            _owner = owner;
            _image = image;
        }

        public IReadOnlyList<ChatTurn> History => _history;

        public async Task<string> SendAsync(string userMessage, CancellationToken ct = default)
        {
            // Visão roda uma única vez na primeira mensagem.
            if (!_visionDone)
            {
                _visionDescription = await _owner.DescribeAsync(_image, ct).ConfigureAwait(false);
                _visionDone = true;
            }

            _history.Add(new ChatTurn("user", userMessage));

            var (systemPrompt, imageForMain) = BuildContext(_visionDescription, _image);
            // Sem visão, reenvia a imagem só no primeiro turno (provider aceita 1 imagem).
            if (_visionDescription is null && _history.Count > 1) imageForMain = null;

            var prompt = BuildPrompt(userMessage);
            var req = new AiRequest(prompt, imageForMain, systemPrompt, maxTokens: 1500);
            var resp = await _owner.CompleteWithFallbackAsync(req, ct).ConfigureAwait(false);

            _history.Add(new ChatTurn("assistant", resp.Text));
            return resp.Text;
        }

        /// <summary>
        /// Monta o prompt com o histórico anterior (continuidade) + a nova mensagem.
        /// </summary>
        private string BuildPrompt(string newMessage)
        {
            if (_history.Count <= 1) return newMessage; // primeiro turno: só a pergunta

            var sb = new StringBuilder();
            sb.AppendLine("Continue a conversa sobre a mesma imagem. Histórico:");
            // todos menos a última (que é a newMessage recém-adicionada)
            for (int i = 0; i < _history.Count - 1; i++)
            {
                var t = _history[i];
                sb.AppendLine($"{(t.Role == "user" ? "Usuário" : "Assistente")}: {t.Text}");
            }
            sb.AppendLine($"Usuário: {newMessage}");
            sb.Append("Assistente:");
            return sb.ToString();
        }
    }
}
