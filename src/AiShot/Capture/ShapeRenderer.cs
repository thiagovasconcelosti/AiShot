using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AiShot.Capture;

/// <summary>Desenho das anotações (shapes) sobre o print. Sem estado.</summary>
internal static class ShapeRenderer
{
    /// <summary>Lado do bloco de pixelização, em pixels da imagem.</summary>
    private const int TamanhoDoBloco = 12;

    /// <summary>true se o shape tem tamanho suficiente para ser mantido.</summary>
    public static bool IsValid(Shape s) => s.Tool switch
    {
        Tool.Pen => s.Points is { Count: > 1 },
        // O marcador de passo é posicionado por clique: não há arraste a medir.
        Tool.Step => true,
        _ => Math.Abs(s.A.X - s.B.X) > 2 || Math.Abs(s.A.Y - s.B.Y) > 2,
    };

    /// <summary>
    /// Desenha um shape no contexto gráfico informado.
    /// </summary>
    /// <param name="origem">
    /// Imagem de onde a pixelização lê os pixels. Sem ela, o borrão é desenhado
    /// como um retângulo opaco — o conteúdo nunca é exibido de qualquer forma.
    /// </param>
    /// <param name="deslocamento">
    /// Diferença entre as coordenadas do shape e as da <paramref name="origem"/>.
    /// Na tela as duas coincidem; na imagem final a origem é o canto da seleção.
    /// </param>
    public static void Draw(Graphics g, Shape s, Bitmap? origem = null, Point deslocamento = default)
    {
        if (s.Tool == Tool.Blur)
        {
            DrawBlur(g, s, origem, deslocamento);
            return;
        }

        if (s.Tool == Tool.Step)
        {
            DrawStep(g, s);
            return;
        }

        using var pen = new Pen(s.Color, s.Thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (s.Tool)
        {
            case Tool.Pen:
                if (s.Points is { Count: > 1 }) g.DrawLines(pen, s.Points.ToArray());
                break;
            case Tool.Line:
                g.DrawLine(pen, s.A, s.B);
                break;
            case Tool.Arrow:
                pen.CustomEndCap = new AdjustableArrowCap(4, 5);
                g.DrawLine(pen, s.A, s.B);
                break;
            case Tool.Rect:
                g.DrawRectangle(pen, SelectionGeometry.Normalize(s.A, s.B));
                break;
            case Tool.Ellipse:
                g.DrawEllipse(pen, SelectionGeometry.Normalize(s.A, s.B));
                break;
            case Tool.Text:
                using (var f = new Font("Segoe UI", 9f + s.Thickness * 3f, FontStyle.Bold))
                using (var b = new SolidBrush(s.Color))
                    g.DrawString(s.TextValue, f, b, s.A);
                break;
        }
    }

    /// <summary>
    /// Pixeliza a região do shape lendo os pixels da imagem de origem.
    /// </summary>
    /// <remarks>
    /// A pixelização é aplicada como blocos opacos de cor sólida, e não como
    /// uma camada translúcida por cima do original. Essa distinção é o ponto da
    /// ferramenta: uma sobreposição semitransparente deixaria o conteúdo
    /// original recuperável na imagem exportada, o que anularia o propósito de
    /// ocultar dados sensíveis antes de compartilhar.
    /// </remarks>
    private static void DrawBlur(Graphics g, Shape s, Bitmap? origem, Point deslocamento)
    {
        var area = SelectionGeometry.Normalize(s.A, s.B);
        if (area.Width <= 0 || area.Height <= 0) return;

        if (origem is null)
        {
            // Sem acesso aos pixels, cobre a área — nunca revela o conteúdo.
            using var solido = new SolidBrush(Color.FromArgb(255, 40, 40, 45));
            g.FillRectangle(solido, area);
            return;
        }

        // Recorta à imagem de origem: um shape pode ter sido desenhado em cima
        // da borda da seleção e extrapolar os limites do bitmap.
        var naOrigem = Rectangle.Intersect(
            new Rectangle(area.X + deslocamento.X, area.Y + deslocamento.Y, area.Width, area.Height),
            new Rectangle(Point.Empty, origem.Size));
        if (naOrigem.Width <= 0 || naOrigem.Height <= 0) return;

        var modoAnterior = g.InterpolationMode;
        var suavizacaoAnterior = g.SmoothingMode;
        var pixelAnterior = g.PixelOffsetMode;
        try
        {
            // Reduzir e ampliar de volta com vizinho mais próximo produz blocos
            // de cor chapada — o mesmo efeito de calcular a média por bloco,
            // porém sem percorrer os pixels um a um.
            int larguraReduzida = Math.Max(1, naOrigem.Width / TamanhoDoBloco);
            int alturaReduzida = Math.Max(1, naOrigem.Height / TamanhoDoBloco);

            using var reduzida = new Bitmap(larguraReduzida, alturaReduzida, PixelFormat.Format32bppArgb);
            using (var gr = Graphics.FromImage(reduzida))
            {
                gr.InterpolationMode = InterpolationMode.HighQualityBilinear; // média ao encolher
                gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
                gr.DrawImage(origem, new Rectangle(0, 0, larguraReduzida, alturaReduzida), naOrigem, GraphicsUnit.Pixel);
            }

            g.InterpolationMode = InterpolationMode.NearestNeighbor; // blocos, sem degradê
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(reduzida, area);
        }
        finally
        {
            g.InterpolationMode = modoAnterior;
            g.SmoothingMode = suavizacaoAnterior;
            g.PixelOffsetMode = pixelAnterior;
        }
    }

    /// <summary>Marcador circular numerado, para prints de passo a passo.</summary>
    private static void DrawStep(Graphics g, Shape s)
    {
        int raio = 12 + s.Thickness * 2;
        var circulo = new Rectangle(s.A.X - raio, s.A.Y - raio, raio * 2, raio * 2);

        var suavizacaoAnterior = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            using (var fundo = new SolidBrush(s.Color))
                g.FillEllipse(fundo, circulo);

            // Contorno claro: garante contraste do círculo contra fundos da mesma
            // cor, sem o qual o marcador some em prints escuros ou saturados.
            using (var contorno = new Pen(Color.White, 2f))
                g.DrawEllipse(contorno, circulo);

            var texto = s.StepNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var fonte = new Font("Segoe UI", raio, FontStyle.Bold, GraphicsUnit.Pixel);
            using var pincel = new SolidBrush(CorLegivelSobre(s.Color));
            using var centralizado = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(texto, fonte, pincel, circulo, centralizado);
        }
        finally { g.SmoothingMode = suavizacaoAnterior; }
    }

    /// <summary>
    /// Preto ou branco, o que tiver mais contraste sobre a cor informada. Usa a
    /// luminância percebida, em que o verde pesa mais que o vermelho e o azul.
    /// </summary>
    private static Color CorLegivelSobre(Color fundo)
    {
        double luminancia = (0.299 * fundo.R + 0.587 * fundo.G + 0.114 * fundo.B) / 255.0;
        return luminancia > 0.5 ? Color.Black : Color.White;
    }
}
