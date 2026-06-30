using System.Net.Http;
using System.Net.Http.Headers;
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
        // Monta o array de "content" da mensagem do usuário: texto e, opcionalmente, imagem.
        var content = new List<object>
        {
            new { type = "text", text = request.Prompt },
        };

        if (request.ImagePng is not null)
        {
            content.Add(new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = "image/png",
                    data = Convert.ToBase64String(request.ImagePng),
                },
            });
        }

        // Corpo da requisição. O "system" só é incluído quando há SystemPrompt.
        var body = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["max_tokens"] = request.MaxTokens,
            ["messages"] = new[]
            {
                new { role = "user", content = content.ToArray() },
            },
        };

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            body["system"] = request.SystemPrompt!;

        var json = JsonSerializer.Serialize(body);

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        httpReq.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        httpReq.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        using var resp = await _http.SendAsync(httpReq, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        // Erro != 2xx: lança com o corpo da resposta para diagnóstico.
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Anthropic retornou {(int)resp.StatusCode} {resp.ReasonPhrase}: {respBody}");

        // Extrai content[0].text do JSON de resposta.
        using var doc = JsonDocument.Parse(respBody);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        return new AiResponse(text, Name, _model);
    }
}
