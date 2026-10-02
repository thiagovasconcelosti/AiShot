using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AiShot.Capture;

/// <summary>
/// Monta a imagem final exportada — o recorte da seleção com as anotações por
/// cima. É o bitmap que vai para a área de transferência, para o arquivo, para
/// o Paint, para o upload, para o OCR e para a IA.
/// </summary>
/// <remarks>
/// <para>
/// Vive fora do <see cref="CaptureOverlay"/> para poder ser verificado sem
/// abrir uma janela: um defeito aqui não aparece na tela, só no que o usuário
/// cola em outro programa.
/// </para>
/// <para>
/// O recorte sem rotação é uma cópia 1:1 por
/// <see cref="Bitmap.Clone(Rectangle, PixelFormat)"/>, e não por um
/// <c>Graphics.DrawImage</c> de um retângulo de origem para um bitmap novo.
/// Nesta segunda forma o GDI+ amostra a vizinhança do retângulo e mistura a
/// primeira linha e a primeira coluna com o que está fora dele — que não existe
/// —, deixando as bordas com metade do alfa (e o canto com um quarto). O
/// resíduo é invisível sobre fundo claro e vira sombra assim que o print é
/// colado sobre fundo escuro. Clone copia os pixels como estão.
/// </para>
/// <para>
/// Sem rotação a saída é opaca (<see cref="PixelFormat.Format32bppRgb"/>): print
/// não tem transparência, e um bitmap sem canal alfa não deixa nenhuma borda
/// translúcida chegar ao arquivo, seja qual for o desenho que venha por cima.
/// Com a moldura girada há alfa de propósito, e só nos cantos que sobram fora do
/// print girado — ali não há conteúdo, e trazer a tela que estava em volta da
/// seleção seria inventar imagem.
/// </para>
/// </remarks>
internal static class FinalImageRenderer
{
    /// <summary>
    /// Recorta <paramref name="selection"/> de <paramref name="background"/> e
    /// desenha as anotações por cima, traduzindo as coordenadas do overlay para
    /// as do bitmap.
    /// </summary>
    /// <param name="rotationDegrees">
    /// Rotação da moldura em graus. Diferente de zero, o print é girado no mesmo
    /// sentido da moldura e a saída cresce para a caixa que o envolve — o print
    /// inteiro sai no arquivo, sem corte, e o que sobra fora dele fica
    /// transparente.
    /// </param>
    public static Bitmap Render(
        Bitmap background, Rectangle selection, IEnumerable<Shape> shapes, double rotationDegrees = 0)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(shapes);

        return rotationDegrees == 0
            ? RenderAlinhado(background, selection, shapes)
            : RenderGirado(background, selection, shapes, rotationDegrees);
    }

    /// <summary>Sem rotação: cópia 1:1 da seleção, pixel a pixel.</summary>
    private static Bitmap RenderAlinhado(Bitmap background, Rectangle selection, IEnumerable<Shape> shapes)
    {
        var recorte = RecorteVisivel(background, selection);

        var bmp = background.Clone(recorte, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        g.TranslateTransform(-recorte.Left, -recorte.Top);
        foreach (var s in shapes) ShapeRenderer.Draw(g, s, background);
        return bmp;
    }

    /// <summary>
    /// Com a moldura girada: o print é girado no mesmo sentido da moldura e o
    /// arquivo é a caixa que envolve o print girado — o print inteiro sai, sem
    /// corte, e o que fica fora dele é transparente.
    /// </summary>
    private static Bitmap RenderGirado(
        Bitmap background, Rectangle selection, IEnumerable<Shape> shapes, double rotationDegrees)
    {
        // Caixa que envolve a seleção girada: é o tamanho do arquivo. Como o giro
        // é em torno do centro, ela fica centrada nele.
        var quadro = SelectionGeometry.FrameBounds(selection, rotationDegrees);
        if (selection.Width <= 0 || selection.Height <= 0 || quadro.Width <= 0 || quadro.Height <= 0)
            throw new ArgumentException("A seleção não tem área dentro do fundo.", nameof(selection));

        // Com canal alfa de propósito: os cantos que sobram do print girado ficam
        // transparentes, em vez de trazer para dentro do arquivo a tela que estava
        // em volta da seleção.
        var bmp = new Bitmap(quadro.Width, quadro.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // Mapa direto: cada pixel da imagem mostra o conteúdo girado no MESMO
        // sentido da moldura. O centro da seleção cai no centro da imagem, e é
        // isso que deixa o print girado inscrito na caixa, sem corte.
        var centro = new PointF(quadro.Left + quadro.Width / 2f, quadro.Top + quadro.Height / 2f);
        g.TranslateTransform(quadro.Width / 2f, quadro.Height / 2f);
        g.RotateTransform((float)rotationDegrees);
        g.TranslateTransform(-centro.X, -centro.Y);

        // Só o print entra na imagem. O recorte é a própria seleção — o mesmo
        // limite que a moldura mostra na tela — e o que fica fora dele continua
        // transparente. As anotações entram pelo mesmo recorte.
        using (var print = new GraphicsPath())
        {
            print.AddRectangle(selection);
            g.SetClip(print);
            g.DrawImage(background, 0, 0);

            // O borrão lê os pixels de background, cujas coordenadas são as do
            // conteúdo: deslocamento zero.
            foreach (var s in shapes) ShapeRenderer.Draw(g, s, background);
            g.ResetClip();
        }

        return bmp;
    }

    /// <summary>
    /// Parte da seleção que existe dentro do fundo. <c>Intersect</c>, e não
    /// <c>Clamp</c>: uma seleção maior que o fundo precisa ser cortada, não
    /// deslocada para dentro dele.
    /// </summary>
    private static Rectangle RecorteVisivel(Bitmap background, Rectangle selection)
    {
        var recorte = Rectangle.Intersect(selection, new Rectangle(Point.Empty, background.Size));
        if (recorte.Width <= 0 || recorte.Height <= 0)
            throw new ArgumentException("A seleção não tem área dentro do fundo.", nameof(selection));

        return recorte;
    }
}
