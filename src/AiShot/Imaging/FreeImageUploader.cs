using System.Net.Http.Headers;
using System.Text.Json;

namespace AiShot.Imaging;

// Uploader para o serviço freeimage.host.
public sealed class FreeImageUploader : IImageUploader
{
    // Chave pública de demonstração documentada pela API do freeimage.host.
    private const string DemoApiKey = "6d207e02198a847aa98d0a2a901485a5";

    private readonly string _apiKey;
    private readonly HttpClient _http;

    public FreeImageUploader(string apiKey, HttpClient http)
    {
        // Usa a chave informada ou cai para a chave de demonstração quando vazia.
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? DemoApiKey : apiKey;
        _http = http;
    }

    public string Name => "freeimage";

    public async Task<UploadResult> UploadAsync(byte[] imagePng, CancellationToken ct = default)
    {
        // Monta o corpo multipart/form-data exigido pela API.
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_apiKey), "key");
        form.Add(new StringContent("upload"), "action");
        form.Add(new StringContent("json"), "format");

        // O arquivo PNG binário vai no campo "source".
        var fileContent = new ByteArrayContent(imagePng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "source", "image.png");

        using var response = await _http.PostAsync("https://freeimage.host/api/1/upload", form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        // Falha: propaga o corpo da resposta no erro.
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Falha no upload (freeimage.host): {(int)response.StatusCode} - {body}");

        using var doc = JsonDocument.Parse(body);
        var image = doc.RootElement.GetProperty("image");

        // DirectUrl: link direto da imagem.
        var directUrl = image.GetProperty("url").GetString() ?? "";

        // PageUrl: prioriza url_viewer; se ausente, usa display_url.
        var pageUrl = TryGetString(image, "url_viewer")
                      ?? TryGetString(image, "display_url")
                      ?? "";

        // Exclusão não é suportada por esta API.
        return new UploadResult(pageUrl, directUrl, "");
    }

    // Retorna a string da propriedade se existir e não for nula; caso contrário, null.
    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return null;
    }
}
