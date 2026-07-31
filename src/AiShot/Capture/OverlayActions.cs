using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace AiShot.Capture;

/// <summary>
/// Ações da barra inferior: copiar, salvar, abrir no Paint, enviar e
/// compartilhar.
/// </summary>
/// <remarks>
/// Recebe a imagem por uma função de renderização e devolve o resultado por
/// mensagens, em vez de tocar na janela. Isso mantém o tratamento de falha de
/// cada ação num só lugar — sem isso, cada caminho repetia o mesmo par de
/// try/catch com texto próprio.
/// </remarks>
internal sealed class OverlayActions
{
    private readonly ICaptureServices _servicos;
    private readonly Func<Bitmap> _renderizar;
    private readonly Action<string> _mensagem;
    private readonly Action _fechar;
    private readonly Func<bool> _descartado;

    public OverlayActions(
        ICaptureServices servicos,
        Func<Bitmap> renderizar,
        Action<string> mensagem,
        Action fechar,
        Func<bool> descartado)
    {
        _servicos = servicos;
        _renderizar = renderizar;
        _mensagem = mensagem;
        _fechar = fechar;
        _descartado = descartado;
    }

    /// <summary>Copia a imagem para a área de transferência.</summary>
    public void Copy()
    {
        try
        {
            // Clipboard.SetImage copia de forma síncrona, então descartar o
            // bitmap logo em seguida é seguro.
            using (var bmp = _renderizar())
                _servicos.CopyToClipboard(bmp);

            if (_servicos.CloseOnCopy) { _fechar(); return; }
            _mensagem("Copiado");
        }
        catch (Exception ex) { _mensagem("Área de transferência indisponível: " + ex.Message); }
    }

    /// <summary>Abre o diálogo de salvar e grava a imagem.</summary>
    public void Save()
    {
        try
        {
            using var bmp = _renderizar();
            _servicos.SaveToFile(bmp);
        }
        catch (Exception ex) { _mensagem("Falha ao salvar: " + ex.Message); }
    }

    /// <summary>Grava a imagem num arquivo temporário e a entrega ao Paint.</summary>
    public void OpenInPaint()
    {
        try
        {
            var caminho = Path.Combine(Path.GetTempPath(), $"aishot_{Guid.NewGuid():N}.png");
            using (var bmp = _renderizar())
                bmp.Save(caminho, ImageFormat.Png);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "mspaint.exe",
                Arguments = $"\"{caminho}\"",
                UseShellExecute = true,
            });
            _fechar(); // entrega ao Paint e sai de cena
        }
        catch (Exception ex) { _mensagem("Falha ao abrir o Paint: " + ex.Message); }
    }

    /// <summary>
    /// Envia a imagem e copia a URL. Com <paramref name="compartilhar"/>,
    /// também abre o resultado no navegador.
    /// </summary>
    public async Task UploadAsync(bool compartilhar, CancellationToken ct)
    {
        try
        {
            _mensagem("Enviando…");

            // O bitmap precisa sobreviver até a conversão em PNG dentro do
            // serviço: descartar antes do await lançaria ObjectDisposedException.
            string url;
            using (var bmp = _renderizar())
                url = await _servicos.UploadAsync(bmp, ct).ConfigureAwait(true);

            if (_descartado()) return;

            CopyText(url);
            if (!compartilhar) { _mensagem("URL copiada: " + url); return; }

            _mensagem(TryOpenUrl(url)
                ? "Aberto no navegador (URL copiada)"
                : "URL inválida; não foi aberta. (copiada)");
        }
        catch (OperationCanceledException) { /* overlay fechado durante o envio */ }
        catch (Exception ex)
        {
            if (!_descartado())
                _mensagem((compartilhar ? "Falha ao compartilhar: " : "Falha no envio: ") + ex.Message);
        }
    }

    /// <summary>
    /// Reconhece o texto da imagem e o copia. Roda no próprio Windows, sem
    /// enviar a imagem para fora.
    /// </summary>
    public async Task CopyTextFromImageAsync(CancellationToken ct)
    {
        try
        {
            _mensagem("Lendo o texto…");

            // O bitmap precisa sobreviver até o fim do reconhecimento, que é
            // assíncrono — o mesmo cuidado do envio.
            string texto;
            using (var bmp = _renderizar())
                texto = await Ocr.TextRecognizer.ExtrairAsync(bmp, ct).ConfigureAwait(true);

            if (_descartado()) return;

            if (string.IsNullOrWhiteSpace(texto))
            {
                _mensagem("Nenhum texto reconhecido na imagem");
                return;
            }

            CopyText(texto);
            var linhas = texto.Split('\n').Length;
            _mensagem(linhas == 1 ? "Texto copiado" : $"Texto copiado ({linhas} linhas)");
        }
        catch (OperationCanceledException) { /* overlay fechado durante a leitura */ }
        catch (Exception ex)
        {
            if (!_descartado()) _mensagem("Falha ao ler o texto: " + ex.Message);
        }
    }

    private void CopyText(string texto)
    {
        try { Clipboard.SetText(texto); }
        catch (Exception ex) { _mensagem("Área de transferência indisponível: " + ex.Message); }
    }

    /// <summary>
    /// Abre a URL apenas se for http ou https. Sem essa checagem, um serviço de
    /// upload comprometido poderia devolver um esquema que o ShellExecute
    /// entregaria a outro programa.
    /// </summary>
    internal static bool TryOpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = u.AbsoluteUri,
            UseShellExecute = true,
        });
        return true;
    }
}
