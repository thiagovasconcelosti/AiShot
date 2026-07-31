using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AiShot.Ai.Providers;

/// <summary>
/// Provider para a API de Chat Completions da OpenAI (POST /v1/chat/completions).
/// </summary>
public sealed class OpenAiProvider : IAiProvider
{
    private const string DefaultBaseUrl = "https://api.openai.com";

    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;
    private readonly HttpClient _http;

    public string Name => "openai";

    public OpenAiProvider(string apiKey, string model, string baseUrl, HttpClient http)
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
                $"OpenAI retornou {(int)resp.StatusCode} {resp.ReasonPhrase}: {HttpUtil.Truncate(respBody)}");

        // Extrai choices[0].message.content do JSON de resposta.
        using var doc = JsonDocument.Parse(respBody);
        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
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
                $"OpenAI retornou {(int)resp.StatusCode} {resp.ReasonPhrase}: {HttpUtil.Truncate(erro)}");
        }

        using var fluxo = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        await foreach (var dados in ServerSentEvents.LerDadosAsync(fluxo, cts.Token).ConfigureAwait(false))
        {
            var pedaco = ServerSentEvents.DeltaDaOpenAi(dados);
            if (!string.IsNullOrEmpty(pedaco)) yield return pedaco;
        }
    }

    /// <summary>Monta a requisição HTTP, com ou sem streaming.</summary>
    private HttpRequestMessage MontarRequisicao(AiRequest request, bool stream)
    {
        // O system prompt vira uma mensagem com role "system" no início.
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            messages.Add(new { role = "system", content = request.SystemPrompt });

        // Cada turno do histórico vira uma mensagem com seu role; imagem (quando
        // presente no turno) entra como bloco image_url (data URL).
        foreach (var m in request.Messages)
        {
            var content = new List<object> { new { type = "text", text = m.Text } };
            if (m.ImagePng is not null)
            {
                var b64 = Convert.ToBase64String(m.ImagePng);
                content.Add(new
                {
                    type = "image_url",
                    image_url = new { url = $"data:image/png;base64,{b64}" },
                });
            }
            messages.Add(new { role = m.Role, content = content.ToArray() });
        }

        var body = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["max_tokens"] = request.MaxTokens,
            ["messages"] = messages.ToArray(),
        };

        if (stream) body["stream"] = true;

        var json = JsonSerializer.Serialize(body);

        var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return httpReq;
    }
}
