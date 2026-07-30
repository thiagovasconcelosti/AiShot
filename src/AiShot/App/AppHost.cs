using System.Diagnostics;
using System.Drawing.Imaging;
using System.Windows.Forms;
using AiShot.Ai;
using AiShot.Capture;
using AiShot.Config;
using AiShot.History;
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
    private readonly CaptureHistory? _history;

    public AppHost(AppConfig cfg, HttpClient http)
    {
        _cfg = cfg;
        _http = http;
        _ai = new AiService(cfg, http);

        // Só existe quando o usuário liga o recurso — sem a chave ativa, nada
        // é gravado em disco.
        if (cfg.History.Enabled)
            _history = new CaptureHistory(CaptureHistory.PastaPadrao, cfg.History.MaxItems, cfg.History.MaxSizeMb);
    }

    public bool CloseOnCopy => _cfg.CloseOnCopy;

    public void CopyToClipboard(Bitmap finalImage)
    {
        Clipboard.SetImage(finalImage);
        Arquivar(finalImage);
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
        Arquivar(finalImage);
    }

    public async Task<string> UploadAsync(Bitmap finalImage, CancellationToken ct = default)
    {
        var png = ToPng(finalImage);
        Arquivar(png);
        var uploader = ImageUploaderFactory.Create(_cfg.ImageUpload, _http);
        var result = await uploader.UploadAsync(png, ct).ConfigureAwait(false);
        return string.IsNullOrEmpty(result.PageUrl) ? result.DirectUrl : result.PageUrl;
    }

    public Ai.IAiChatSession StartChat(Bitmap finalImage)
    {
        var png = ToPng(finalImage);
        Arquivar(png);
        return _ai.CreateSession(png);
    }

    /// <summary>
    /// Guarda a captura no histórico, quando ligado. Uma falha aqui não pode
    /// derrubar a ação que o usuário pediu: ele mandou copiar, e a cópia já
    /// aconteceu.
    /// </summary>
    private void Arquivar(Bitmap finalImage)
    {
        if (_history is null) return;
        Arquivar(ToPng(finalImage));
    }

    private void Arquivar(byte[] png)
    {
        if (_history is null) return;
        try { _history.Adicionar(png, DateTime.Now); }
        catch (Exception ex) { Debug.WriteLine($"AppHost: histórico não gravou: {ex.Message}"); }
    }

    private static byte[] ToPng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
