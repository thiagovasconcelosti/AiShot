using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace AiShot.Ocr;

/// <summary>
/// Extrai o texto de uma imagem usando o reconhecimento do próprio Windows.
/// </summary>
/// <remarks>
/// Roda localmente: a imagem não sai da máquina e o recurso funciona sem rede.
/// Numa ferramenta de captura isso importa — o print costuma ser justamente da
/// mensagem de erro, do código ou do documento que o usuário não quer mandar
/// para fora.
/// </remarks>
[SupportedOSPlatform("windows10.0.19041.0")]
public static class TextRecognizer
{
    /// <summary>
    /// Se o Windows tem um idioma de reconhecimento instalado. Sem nenhum, o
    /// recurso não pode ser oferecido.
    /// </summary>
    public static bool Disponivel => OcrEngine.TryCreateFromUserProfileLanguages() is not null;

    /// <summary>
    /// Reconhece o texto da imagem. Devolve string vazia quando não há texto
    /// reconhecível.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Nenhum idioma de reconhecimento instalado no Windows.
    /// </exception>
    public static async Task<string> ExtrairAsync(Bitmap imagem, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imagem);

        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new NotSupportedException(
                "O Windows não tem nenhum idioma de reconhecimento de texto instalado. "
                + "Adicione um em Configurações → Hora e Idioma → Idioma.");

        // O OCR trabalha sobre SoftwareBitmap; a ponte a partir do GDI+ passa
        // por PNG em memória, que é o formato que o BitmapDecoder aceita sem
        // depender do layout de pixels do Bitmap de origem.
        using var soft = await ParaSoftwareBitmapAsync(imagem, ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        var resultado = await engine.RecognizeAsync(soft).AsTask(ct).ConfigureAwait(false);

        // Result.Text junta tudo numa linha só; as linhas preservam a quebra,
        // que é o que faz um trecho de código colado continuar legível.
        return string.Join(Environment.NewLine, resultado.Lines.Select(l => l.Text));
    }

    private static async Task<SoftwareBitmap> ParaSoftwareBitmapAsync(Bitmap imagem, CancellationToken ct)
    {
        byte[] png;
        using (var ms = new MemoryStream())
        {
            imagem.Save(ms, ImageFormat.Png);
            png = ms.ToArray();
        }

        using var fluxo = new InMemoryRandomAccessStream();
        await fluxo.WriteAsync(png.AsBuffer()).AsTask(ct).ConfigureAwait(false);
        fluxo.Seek(0);

        var decodificador = await BitmapDecoder.CreateAsync(fluxo).AsTask(ct).ConfigureAwait(false);
        return await decodificador.GetSoftwareBitmapAsync().AsTask(ct).ConfigureAwait(false);
    }
}
