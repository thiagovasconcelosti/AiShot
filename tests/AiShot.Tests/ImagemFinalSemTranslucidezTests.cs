using System.Drawing;
using System.Drawing.Imaging;
using AiShot.Capture;

namespace AiShot.Tests;

/// <summary>
/// A imagem exportada precisa sair opaca e com as bordas intactas.
/// </summary>
/// <remarks>
/// O recorte 1:1 do fundo deixava a primeira linha e a primeira coluna com
/// metade do alfa — e o canto com um quarto —, porque o GDI+ amostra a
/// vizinhança do retângulo de origem e mistura essas bordas com o que está fora
/// dele. O resíduo é invisível sobre fundo claro e aparece como sombra quando o
/// print é colado sobre fundo escuro.
/// </remarks>
public class ImagemFinalSemTranslucidezTests
{
    /// <summary>
    /// Fundo opaco com cor variando em cada eixo: qualquer pixel que não venha
    /// do lugar certo fica detectável, o que um fundo uniforme esconderia.
    /// </summary>
    private static Bitmap CriarFundo(int largura, int altura)
    {
        var bmp = new Bitmap(largura, altura, PixelFormat.Format32bppArgb);
        for (int y = 0; y < altura; y++)
            for (int x = 0; x < largura; x++)
                bmp.SetPixel(x, y, Color.FromArgb(255, (x * 7) % 256, (y * 11) % 256, ((x + y) * 13) % 256));
        return bmp;
    }

    [Fact]
    public void ImagemFinal_SaiOpaca_EmTodosOsPixels()
    {
        using var fundo = CriarFundo(120, 90);
        var selecao = new Rectangle(17, 23, 60, 40);

        using var final = FinalImageRenderer.Render(fundo, selecao, []);

        for (int y = 0; y < final.Height; y++)
            for (int x = 0; x < final.Width; x++)
                Assert.Equal(255, final.GetPixel(x, y).A);
    }

    [Fact]
    public void ImagemFinal_PrimeiraLinhaEColuna_ReproduzemOFundo()
    {
        using var fundo = CriarFundo(120, 90);
        var selecao = new Rectangle(17, 23, 60, 40);

        using var final = FinalImageRenderer.Render(fundo, selecao, []);

        // As bordas são exatamente o que o recorte estragava: a cor tem de ser a
        // do pixel correspondente do fundo, e não uma mistura com o lado de fora.
        for (int x = 0; x < final.Width; x++)
            Assert.Equal(fundo.GetPixel(selecao.Left + x, selecao.Top).ToArgb(), final.GetPixel(x, 0).ToArgb());
        for (int y = 0; y < final.Height; y++)
            Assert.Equal(fundo.GetPixel(selecao.Left, selecao.Top + y).ToArgb(), final.GetPixel(0, y).ToArgb());

        Assert.Equal(
            fundo.GetPixel(selecao.Left, selecao.Top).ToArgb(),
            final.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void PngSalvo_NaoGuardaBordaTranslucida()
    {
        using var fundo = CriarFundo(120, 90);
        var selecao = new Rectangle(17, 23, 60, 40);

        byte[] png;
        using (var final = FinalImageRenderer.Render(fundo, selecao, []))
        using (var ms = new MemoryStream())
        {
            final.Save(ms, ImageFormat.Png);
            png = ms.ToArray();
        }

        // O arquivo é o que o usuário cola em outro programa: é nele que a borda
        // translúcida virava sombra. O codificador PNG do GDI+ ainda grava um
        // canal alfa, mas com todos os pixels opacos — o que importa não é o
        // formato, e sim não sobrar pixel translúcido.
        using var lido = new Bitmap(new MemoryStream(png));

        for (int y = 0; y < lido.Height; y++)
            for (int x = 0; x < lido.Width; x++)
                Assert.Equal(255, lido.GetPixel(x, y).A);
    }

    /// <summary>
    /// Reproduz uma captura de verdade: copia a tela para o mesmo bitmap ARGB
    /// que o overlay usa e exporta pelo caminho de produção. É esta a imagem que
    /// o usuário cola em outro programa.
    /// </summary>
    /// <remarks>
    /// A cópia da tela é do GDI, e não do GDI+: se ela deixar o alfa do bitmap
    /// de origem em zero, o canal alfa não pode sobreviver até o arquivo.
    /// </remarks>
    [SkippableFact]
    public void CapturaDaTelaExportada_SaiSemBordaTranslucida()
    {
        var tela = System.Windows.Forms.SystemInformation.VirtualScreen;
        Skip.If(tela.Width <= 0 || tela.Height <= 0, "Requer uma sessão com área de trabalho.");

        using var fundo = new Bitmap(tela.Width, tela.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(fundo))
            g.CopyFromScreen(tela.Left, tela.Top, 0, 0, tela.Size);

        byte[] png;
        using (var final = FinalImageRenderer.Render(fundo, new Rectangle(10, 10, 40, 30), []))
        using (var ms = new MemoryStream())
        {
            final.Save(ms, ImageFormat.Png);
            png = ms.ToArray();
        }

        using var lido = new Bitmap(new MemoryStream(png));
        for (int y = 0; y < lido.Height; y++)
            for (int x = 0; x < lido.Width; x++)
                Assert.Equal(255, lido.GetPixel(x, y).A);
    }

    [Fact]
    public void SelecaoMaiorQueOFundo_EhCortada()
    {
        using var fundo = CriarFundo(40, 30);

        using var final = FinalImageRenderer.Render(fundo, new Rectangle(10, 10, 100, 100), []);

        Assert.Equal(30, final.Width);
        Assert.Equal(20, final.Height);
    }
}
