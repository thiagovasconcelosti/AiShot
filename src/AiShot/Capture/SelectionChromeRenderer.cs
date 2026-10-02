using System.Drawing;
using System.Drawing.Drawing2D;
using AiShot.UI;

namespace AiShot.Capture;

/// <summary>
/// Desenho da moldura da seleção: o contorno (girado ou não), as oito alças de
/// redimensionamento e a alça de rotação.
/// </summary>
/// <remarks>
/// Sem estado, como os outros renderizadores: recebe a seleção e o ângulo. Só a
/// moldura e a alça de rotação acompanham o ângulo — as barras de ferramentas
/// continuam desenhadas alinhadas à tela, na posição de sempre.
/// </remarks>
internal static class SelectionChromeRenderer
{
    private static readonly StringFormat CenterFmt =
        new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    /// <summary>Traça a moldura e, quando a edição está ativa, as alças.</summary>
    public static void Draw(Graphics g, Rectangle selecao, double rotacao, bool comAlcas)
    {
        if (selecao.Width <= 0) return;

        using var pen = new Pen(Theme.SelectionStroke, 1.5f);
        g.SmoothingMode = SmoothingMode.None;

        // Sem rotação o traço é o mesmo de sempre; com rotação, o polígono dos
        // quatro cantos girados.
        if (rotacao == 0) g.DrawRectangle(pen, selecao);
        else g.DrawPolygon(pen, SelectionGeometry.Corners(selecao, rotacao));

        if (!comAlcas) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var hr in SelectionGeometry.RotatedHandleRects(selecao, rotacao))
        {
            using var fill = new SolidBrush(Color.White);
            using var br = new Pen(Color.FromArgb(120, 0, 0, 0), 1);
            using var p = Theme.RoundRect(hr, 2);
            g.FillPath(fill, p);
            g.DrawPath(br, p);
        }

        DrawRotationHandle(g, selecao, rotacao);
    }

    /// <summary>
    /// Alça de rotação: uma bolinha acima do meio da aresta superior, presa a ela
    /// por um risco. Gira junto com a moldura — é a única peça da interface, além
    /// da própria moldura, que acompanha o ângulo.
    /// </summary>
    public static void DrawRotationHandle(Graphics g, Rectangle selecao, double rotacao)
    {
        var centro = SelectionGeometry.Center(selecao);
        var meioDaAresta = SelectionGeometry.RotatePoint(
            new PointF(selecao.Left + selecao.Width / 2f, selecao.Top), centro, rotacao);
        var naTela = SelectionGeometry.RotatePoint(SelectionGeometry.RotationHandle(selecao), centro, rotacao);

        const int raio = 9;
        var circulo = new RectangleF(naTela.X - raio, naTela.Y - raio, raio * 2, raio * 2);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var haste = new Pen(Theme.SelectionStroke, 1.2f))
            g.DrawLine(haste, meioDaAresta, naTela);
        using (var fundo = new SolidBrush(Theme.Surface))
            g.FillEllipse(fundo, circulo);
        using (var borda = new Pen(Theme.Border, 1))
            g.DrawEllipse(borda, circulo);
        using (var glifo = new SolidBrush(Theme.Text))
            g.DrawString(Icons.Redo, Icons.Cached(14), glifo, circulo, CenterFmt);
    }
}
