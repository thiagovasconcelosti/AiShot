using System.Drawing.Imaging;
using System.Windows.Forms;
using AiShot.Ai;
using AiShot.Capture;
using AiShot.Config;
using AiShot.Imaging;

namespace AiShot.App;

/// <summary>
/// Implementa os serviços consumidos pelo editor (copiar, salvar, upload, IA).
/// Faz a ponte entre a UI e as camadas de IA / upload.
/// </summary>
public sealed class AppHost : ICaptureServices
{
    private readonly AppConfig _cfg;
    private readonly HttpClient _http;
    private readonly IAiService _ai;

    public AppHost(AppConfig cfg, HttpClient http)
    {
        _cfg = cfg;
        _http = http;
        _ai = new AiService(cfg, http);
    }

    public void CopyToClipboard(Bitmap finalImage)
    {
        Clipboard.SetImage(finalImage);
    }

    public void SaveToFile(Bitmap finalImage)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg",
            FileName = $"aishot_{DateTime.Now:yyyyMMdd_HHmmss}.png",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
        var fmt = ext == ".jpg" || ext == ".jpeg" ? ImageFormat.Jpeg : ImageFormat.Png;
        finalImage.Save(dlg.FileName, fmt);
    }

    public async Task<string> UploadAsync(Bitmap finalImage, CancellationToken ct = default)
    {
        var png = ToPng(finalImage);
        var uploader = ImageUploaderFactory.Create(_cfg.ImageUpload, _http);
        var result = await uploader.UploadAsync(png, ct).ConfigureAwait(false);
        return string.IsNullOrEmpty(result.PageUrl) ? result.DirectUrl : result.PageUrl;
    }

    public Ai.IAiChatSession StartChat(Bitmap finalImage)
    {
        var png = ToPng(finalImage);
        return _ai.CreateSession(png);
    }

    private static byte[] ToPng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
