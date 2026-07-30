using System.Drawing;
using System.Drawing.Imaging;
using AiShot.Ocr;

namespace AiShot.Tests;

/// <summary>
/// Reconhecimento de texto local (Windows.Media.Ocr).
/// </summary>
/// <remarks>
/// Depende de um idioma de reconhecimento instalado no Windows. Onde não
/// houver, os testes são ignorados em vez de falhar — a ausência é uma
/// configuração da máquina, não um defeito do código.
/// </remarks>
public class TextRecognizerTests
{
    private static void ExigirOcr() =>
        Skip.IfNot(TextRecognizer.Disponivel,
            "Nenhum idioma de reconhecimento de texto instalado no Windows.");

    /// <summary>Imagem com o texto informado, em tamanho legível para o OCR.</summary>
    private static Bitmap ComTexto(string texto, int largura = 640, int altura = 160)
    {
        var bmp = new Bitmap(largura, altura, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var f = new Font("Segoe UI", 30);
        g.DrawString(texto, f, Brushes.Black, 10, 20);
        return bmp;
    }

    [SkippableFact]
    public async Task ExtrairAsync_ReconheceOTextoDaImagem()
    {
        ExigirOcr();
        using var img = ComTexto("Erro 404");

        var texto = await TextRecognizer.ExtrairAsync(img);

        Assert.Contains("404", texto, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task ExtrairAsync_PreservaAQuebraEntreLinhas()
    {
        // Result.Text junta tudo numa linha só; a quebra é o que mantém um
        // trecho de código colado legível.
        ExigirOcr();

        using var img = new Bitmap(640, 240, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(img))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var f = new Font("Segoe UI", 30);
            g.DrawString("primeira linha", f, Brushes.Black, 10, 20);
            g.DrawString("segunda linha", f, Brushes.Black, 10, 120);
        }

        var texto = await TextRecognizer.ExtrairAsync(img);

        Assert.Contains(Environment.NewLine, texto, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task ExtrairAsync_ComImagemSemTexto_DevolveVazio()
    {
        // Critério de aceite: falhar de forma clara quando nada é reconhecido.
        // "Vazio" é o sinal que a camada de ação usa para avisar o usuário.
        ExigirOcr();

        using var img = new Bitmap(200, 120, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(img)) g.Clear(Color.White);

        Assert.True(string.IsNullOrWhiteSpace(await TextRecognizer.ExtrairAsync(img)));
    }

    [SkippableFact]
    public async Task ExtrairAsync_ComImagemMinima_NaoLanca()
    {
        // Uma seleção de poucos pixels não pode derrubar o aplicativo.
        ExigirOcr();
        using var img = new Bitmap(1, 1, PixelFormat.Format32bppArgb);

        Assert.True(string.IsNullOrWhiteSpace(await TextRecognizer.ExtrairAsync(img)));
    }

    [Fact]
    public async Task ExtrairAsync_ComImagemNula_Lanca() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => TextRecognizer.ExtrairAsync(null!));

    [SkippableFact]
    public async Task ExtrairAsync_ComCancelamentoJaPedido_Lanca()
    {
        ExigirOcr();
        using var img = ComTexto("qualquer coisa");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TextRecognizer.ExtrairAsync(img, cts.Token));
    }

    [Fact]
    public void Disponivel_NaoLanca()
    {
        // Consultado antes de oferecer o recurso; precisa responder mesmo numa
        // máquina sem nenhum idioma instalado.
        _ = TextRecognizer.Disponivel;
    }
}
