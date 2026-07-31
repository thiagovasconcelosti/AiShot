using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AiShot.Ai.Providers;

/// <summary>
/// Provider para a API de Mensagens da Anthropic (POST /v1/messages).
/// </summary>
public sealed class AnthropicProvider : IAiProvider
{
    private const string DefaultBaseUrl = "https://api.anthropic.com";

    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;
    private readonly HttpClient _http;

    public string Name => "anthropic";

    public AnthropicProvider(string apiKey, string model, string baseUrl, HttpClient http)
    {
        _apiKey = apiKey;
        _model = model;
        // Usa a URL base padrão quando não informada.
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/');
        _http = http;
    }

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        using var httpReq = MontarRequisicao(request, stream: false);

        using var cts = HttpUtil.Timeout(ct, TimeSpan.FromMinutes(2));
        using var resp = await _http.SendAsync(httpReq, cts.Token).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

        // Erro != 2xx: lança com o corpo da resposta para diagnóstico.
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Anthropic retornou {(int)resp.StatusCode} {resp.ReasonPhrase}: {HttpUtil.Truncate(respBody)}");

        // Extrai content[0].text do JSON de resposta.
        using var doc = JsonDocument.Parse(respBody);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        return new AiResponse(text, Name, _model);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        AiRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var httpReq = MontarRequisicao(request, stream: true);

        // O timeout cobre o fluxo inteiro, não só o cabeçalho: uma conexão que
        // trave no meio precisa ser cortada tanto quanto uma que nunca responda.
        using var cts = HttpUtil.Timeout(ct, TimeSpan.FromMinutes(5));
        using var resp = await _http
            .SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, cts.Token)
            .ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var erro = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Anthropic retornou {(int)resp.StatusCode} {resp.ReasonPhrase}: {HttpUtil.Truncate(erro)}");
        }

        using var fluxo = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        await foreach (var dados in ServerSentEvents.LerDadosAsync(fluxo, cts.Token).ConfigureAwait(false))
        {
            var pedaco = ServerSentEvents.DeltaDaAnthropic(dados);
            if (!string.IsNullOrEmpty(pedaco)) yield return pedaco;
        }
    }

    /// <summary>Monta a requisição HTTP, com ou sem streaming.</summary>
    private HttpRequestMessage MontarRequisicao(AiRequest request, bool stream)
    {
        // Traduz cada turno do histórico para uma entrada de messages[] com seu role.
        // A imagem (quando presente num turno) vira um bloco "image" no content daquele turno.
        var messages = new List<object>(request.Messages.Count);
        foreach (var m in request.Messages)
        {
            var content = new List<object> { new { type = "text", text = m.Text } };
            if (m.ImagePng is not null)
            {
                content.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = "image/png",
                        data = Convert.ToBase64String(m.ImagePng),
                    },
                });
            }
            messages.Add(new { role = m.Role, content = content.ToArray() });
        }

        // Corpo da requisição. O "system" só é incluído quando há SystemPrompt.
        var body = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["max_tokens"] = request.MaxTokens,
            ["messages"] = messages.ToArray(),
        };

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            body["system"] = request.SystemPrompt!;

        if (stream) body["stream"] = true;

        var json = JsonSerializer.Serialize(body);

        var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        httpReq.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        httpReq.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        return httpReq;
    }
}
