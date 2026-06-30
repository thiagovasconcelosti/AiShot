using System.Text.Json;

namespace AiShot.Imaging;

// Uploader para o serviço imgbb.com.
public sealed class ImgbbUploader : IImageUploader
{
    private readonly string _apiKey;
    private readonly HttpClient _http;

    public ImgbbUploader(string apiKey, HttpClient http)
    {
        _apiKey = apiKey;
        _http = http;
    }

    public string Name => "imgbb";

    public async Task<UploadResult> UploadAsync(byte[] imagePng, CancellationToken ct = default)
    {
        // A API do imgbb recebe a imagem como base64 no campo "image".
        var base64 = Convert.ToBase64String(imagePng);

        using var form = new MultipartFormDataContent
        {
            { new StringContent(base64), "image" }
        };

        var url = $"https://api.imgbb.com/1/upload?key={Uri.EscapeDataString(_apiKey)}";
        using var response = await _http.PostAsync(url, form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        // Falha: propaga o corpo da resposta no erro.
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Falha no upload (imgbb): {(int)response.StatusCode} - {HttpUtil.Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");

        var directUrl = data.GetProperty("url").GetString() ?? "";
        var pageUrl = data.GetProperty("url_viewer").GetString() ?? "";
        var deleteUrl = data.GetProperty("delete_url").GetString() ?? "";

        return new UploadResult(pageUrl, directUrl, deleteUrl);
    }
}
