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
/// O recorte é uma cópia 1:1 por <see cref="Bitmap.Clone(Rectangle, PixelFormat)"/>,
/// e não por um <c>Graphics.DrawImage</c> de um retângulo de origem para um
/// bitmap novo. Nesta segunda forma o GDI+ amostra a vizinhança do retângulo e
/// mistura a primeira linha e a primeira coluna com o que está fora dele — que
/// não existe —, deixando as bordas com metade do alfa (e o canto com um
/// quarto). O resíduo é invisível sobre fundo claro e vira sombra assim que o
/// print é colado sobre fundo escuro. Clone copia os pixels como estão.
/// </para>
/// <para>
/// A saída é opaca (<see cref="PixelFormat.Format32bppRgb"/>): print não tem
/// transparência, e um bitmap sem canal alfa não deixa nenhuma borda translúcida
/// chegar ao arquivo, seja qual for o desenho que venha por cima.
/// </para>
/// </remarks>
internal static class FinalImageRenderer
{
    /// <summary>
    /// Recorta <paramref name="selection"/> de <paramref name="background"/> e
    /// desenha as anotações por cima, traduzindo as coordenadas do overlay para
    /// as do bitmap.
    /// </summary>
    public static Bitmap Render(Bitmap background, Rectangle selection, IEnumerable<Shape> shapes)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(shapes);

        // Intersect, e não Clamp: uma seleção maior que o fundo precisa ser
        // cortada, não deslocada para dentro dele.
        var recorte = Rectangle.Intersect(selection, new Rectangle(Point.Empty, background.Size));
        if (recorte.Width <= 0 || recorte.Height <= 0)
            throw new ArgumentException("A seleção não tem área dentro do fundo.", nameof(selection));

        var bmp = background.Clone(recorte, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // A partir daqui o Graphics converte coordenadas do overlay para as do
        // bitmap; os shapes seguem sendo desenhados em coordenadas do overlay,
        // que são as mesmas de background — daí o deslocamento zero na leitura
        // dos pixels pelo borrão.
        g.TranslateTransform(-recorte.Left, -recorte.Top);
        foreach (var s in shapes) ShapeRenderer.Draw(g, s, background);
        return bmp;
    }
}
