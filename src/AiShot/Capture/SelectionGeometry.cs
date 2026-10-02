using System.Drawing;
using System.Drawing.Drawing2D;

namespace AiShot.Capture;

/// <summary>
/// Geometria pura da seleção (sem estado/UI) — funções testáveis sobre
/// <see cref="Rectangle"/>. Usada pelo overlay para hit-test, resize/move e
/// para a moldura girada.
/// </summary>
/// <remarks>
/// <para>
/// A seleção é guardada sempre alinhada aos eixos; a rotação é um ângulo à
/// parte, aplicado em torno do centro. Quem gira é só a moldura: o conteúdo do
/// print continua na orientação da tela, e é o arquivo exportado que sai
/// endireitado (ver <see cref="FinalImageRenderer"/>).
/// </para>
/// <para>
/// As funções "local" convertem um ponto da tela para o sistema sem rotação da
/// moldura. Como a moldura é o próprio retângulo girado em torno do centro, isso
/// permite reaproveitar o mesmo hit-test alinhado aos eixos em qualquer ângulo —
/// nenhuma alça precisa de matemática própria.
/// </para>
/// </remarks>
internal static class SelectionGeometry
{
    public const int MinSize = 16;
    private const int HandleSize = 9;

    /// <summary>Distância da alça de rotação até o meio da aresta superior.</summary>
    public const int RotationHandleOffset = 30;

    /// <summary>
    /// Raio de acerto da alça de rotação. É maior que o desenho de propósito: a
    /// alça é pequena e fica fora da moldura, onde não há mais nada para acertar.
    /// </summary>
    public const int RotationHandleRadius = 12;

    /// <summary>Retângulo normalizado entre dois pontos (cantos em qualquer ordem).</summary>
    public static Rectangle Normalize(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    /// <summary>Centro da seleção — o eixo em torno do qual a moldura gira.</summary>
    public static PointF Center(Rectangle r) => new(r.Left + r.Width / 2f, r.Top + r.Height / 2f);

    /// <summary>Gira um ponto em torno de um centro, em graus (sentido horário na tela).</summary>
    public static PointF RotatePoint(PointF ponto, PointF centro, double graus)
    {
        if (graus == 0) return ponto;

        double rad = graus * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        double dx = ponto.X - centro.X, dy = ponto.Y - centro.Y;
        return new PointF(
            (float)(centro.X + dx * cos - dy * sin),
            (float)(centro.Y + dx * sin + dy * cos));
    }

    /// <summary>
    /// Leva um ponto da tela para o sistema da moldura (o retângulo sem rotação).
    /// É o que deixa o hit-test das alças igual em qualquer ângulo.
    /// </summary>
    public static PointF ToLocal(Point ponto, Rectangle selecao, double graus) =>
        graus == 0
            ? new PointF(ponto.X, ponto.Y)
            : RotatePoint(new PointF(ponto.X, ponto.Y), Center(selecao), -graus);

    /// <summary>O ponto local está dentro da seleção? (<see cref="Rectangle"/> só aceita inteiros.)</summary>
    public static bool Contains(Rectangle selecao, PointF local) =>
        local.X >= selecao.Left && local.X < selecao.Right
        && local.Y >= selecao.Top && local.Y < selecao.Bottom;

    /// <summary>Posição da alça de rotação, no sistema local: acima do meio da aresta superior.</summary>
    public static PointF RotationHandle(Rectangle selecao) =>
        new(selecao.Left + selecao.Width / 2f, selecao.Top - RotationHandleOffset);

    /// <summary>
    /// Normaliza o ângulo para [0, 360) e aplica os ímãs do arraste: com
    /// <paramref name="passosDe15"/> (Shift), passos de 15°; sem ele, gruda em
    /// 0/90/180/270 quando o arraste chega perto — sair de um ângulo reto por um
    /// grau é fácil, e o recorte sairia torto sem o usuário perceber.
    /// </summary>
    public static double SnapRotation(double graus, bool passosDe15)
    {
        graus = ((graus % 360) + 360) % 360;
        if (passosDe15) return Math.Round(graus / 15.0) * 15.0 % 360;

        double reto = Math.Round(graus / 90.0) * 90.0;
        return Math.Abs(graus - reto) <= 2.5 ? reto % 360 : graus;
    }

    /// <summary>O ponto local está sobre a alça de rotação?</summary>
    public static bool HitRotationHandle(Rectangle selecao, PointF local)
    {
        var alca = RotationHandle(selecao);
        float dx = local.X - alca.X, dy = local.Y - alca.Y;
        return dx * dx + dy * dy <= (float)RotationHandleRadius * RotationHandleRadius;
    }

    /// <summary>Os quatro cantos da moldura já girados, na ordem TL, TR, BR, BL.</summary>
    public static PointF[] Corners(Rectangle selecao, double graus)
    {
        PointF[] cantos =
        [
            new(selecao.Left, selecao.Top),
            new(selecao.Right, selecao.Top),
            new(selecao.Right, selecao.Bottom),
            new(selecao.Left, selecao.Bottom),
        ];

        if (graus == 0) return cantos;

        var centro = Center(selecao);
        for (int i = 0; i < cantos.Length; i++) cantos[i] = RotatePoint(cantos[i], centro, graus);
        return cantos;
    }

    /// <summary>Caminho fechado da moldura (o retângulo girado), em coordenadas da tela.</summary>
    public static GraphicsPath FramePath(Rectangle selecao, double graus)
    {
        var caminho = new GraphicsPath();
        caminho.AddPolygon(Corners(selecao, graus));
        return caminho;
    }

    /// <summary>
    /// Menor retângulo alinhado aos eixos que contém a moldura girada — a área
    /// da tela que o recorte rotacionado precisa ler.
    /// </summary>
    public static Rectangle FrameBounds(Rectangle selecao, double graus)
    {
        if (graus == 0) return selecao;

        var cantos = Corners(selecao, graus);
        float minX = cantos[0].X, maxX = cantos[0].X, minY = cantos[0].Y, maxY = cantos[0].Y;
        foreach (var c in cantos)
        {
            minX = Math.Min(minX, c.X); maxX = Math.Max(maxX, c.X);
            minY = Math.Min(minY, c.Y); maxY = Math.Max(maxY, c.Y);
        }

        return Rectangle.FromLTRB(
            (int)Math.Floor(minX), (int)Math.Floor(minY),
            (int)Math.Ceiling(maxX), (int)Math.Ceiling(maxY));
    }

    /// <summary>
    /// Pontos de referência das 8 alças (cantos + meios), na ordem
    /// TL,T,TR,R,BR,B,BL,L. São inteiros porque viram alças e acertos de mouse.
    /// </summary>
    public static Point[] HandlePoints(Rectangle r) =>
    [
        new(r.Left, r.Top), new(r.Left + r.Width / 2, r.Top), new(r.Right, r.Top),
        new(r.Right, r.Top + r.Height / 2), new(r.Right, r.Bottom),
        new(r.Left + r.Width / 2, r.Bottom), new(r.Left, r.Bottom), new(r.Left, r.Top + r.Height / 2),
    ];

    /// <summary>As 8 alças (quadrados de 9 px) da seleção sem rotação.</summary>
    public static Rectangle[] HandleRects(Rectangle r)
    {
        int h = HandleSize / 2;
        return HandlePoints(r).Select(p => new Rectangle(p.X - h, p.Y - h, HandleSize, HandleSize)).ToArray();
    }

    /// <summary>
    /// As 8 alças com os centros já girados. As alças continuam quadradas e
    /// alinhadas à tela — girar cada quadradinho só dificultaria o acerto.
    /// </summary>
    public static Rectangle[] RotatedHandleRects(Rectangle selecao, double graus)
    {
        if (graus == 0) return HandleRects(selecao);

        int h = HandleSize / 2;
        var centro = Center(selecao);
        var pontos = HandlePoints(selecao);
        var saida = new Rectangle[pontos.Length];
        for (int i = 0; i < pontos.Length; i++)
        {
            var girado = RotatePoint(pontos[i], centro, graus);
            saida[i] = new Rectangle(
                (int)Math.Round(girado.X) - h, (int)Math.Round(girado.Y) - h, HandleSize, HandleSize);
        }
        return saida;
    }

    /// <summary>Qual alça está sob o ponto (ou None).</summary>
    public static ResizeHandle HitHandle(Rectangle sel, Point p)
    {
        var rects = HandleRects(sel);
        ResizeHandle[] order = { ResizeHandle.TL, ResizeHandle.T, ResizeHandle.TR, ResizeHandle.R, ResizeHandle.BR, ResizeHandle.B, ResizeHandle.BL, ResizeHandle.L };
        for (int i = 0; i < rects.Length; i++)
            if (rects[i].Contains(p)) return order[i];
        return ResizeHandle.None;
    }

    /// <summary>Qual alça está sob o ponto local (a tela passa por <see cref="ToLocal"/>).</summary>
    public static ResizeHandle HitHandle(Rectangle sel, PointF local) =>
        HitHandle(sel, new Point((int)Math.Round(local.X), (int)Math.Round(local.Y)));

    /// <summary>A alça muda a largura da seleção?</summary>
    private static bool AlteraX(ResizeHandle alca) => alca
        is ResizeHandle.TL or ResizeHandle.TR or ResizeHandle.BL or ResizeHandle.BR or ResizeHandle.L or ResizeHandle.R;

    /// <summary>A alça muda a altura da seleção?</summary>
    private static bool AlteraY(ResizeHandle alca) => alca
        is ResizeHandle.TL or ResizeHandle.TR or ResizeHandle.BL or ResizeHandle.BR or ResizeHandle.T or ResizeHandle.B;

    /// <summary>
    /// Ponto que fica parado durante o arraste da alça — o canto oposto ou o meio
    /// da aresta oposta —, em coordenadas da tela. Com a moldura girada ele
    /// também está girado; é ele que mantém o lado oposto no lugar.
    /// </summary>
    public static PointF Anchor(ResizeHandle alca, Rectangle selecao, double graus)
    {
        var pontos = HandlePoints(selecao);
        int oposta = alca switch
        {
            ResizeHandle.TL => 4, // BR
            ResizeHandle.T  => 5, // B
            ResizeHandle.TR => 6, // BL
            ResizeHandle.R  => 7, // L
            ResizeHandle.BR => 0, // TL
            ResizeHandle.B  => 1, // T
            ResizeHandle.BL => 2, // TR
            ResizeHandle.L  => 3, // R
            _ => 0,
        };

        PointF local = alca is ResizeHandle.None or ResizeHandle.Move or ResizeHandle.Rotate
            ? new PointF(selecao.Left, selecao.Top)
            : new PointF(pontos[oposta].X, pontos[oposta].Y);

        return graus == 0 ? local : RotatePoint(local, Center(selecao), graus);
    }

    /// <summary>
    /// Aplica o arraste de uma alça (ou o movimento da seleção) à moldura, com ou
    /// sem rotação.
    /// </summary>
    /// <param name="ancora">
    /// Em <see cref="ResizeHandle.Move"/>, é o ponto onde o arraste começou (o
    /// deslocamento é o do mouse na tela). Nas demais, é o ponto fixo devolvido
    /// por <see cref="Anchor"/>.
    /// </param>
    /// <returns>
    /// A nova seleção, ou <see cref="Rectangle.Empty"/> quando o resultado ficaria
    /// menor que <see cref="MinSize"/> — o chamador mantém a seleção atual.
    /// </returns>
    /// <remarks>
    /// O arraste é projetado nos eixos da moldura e o centro é recalculado a
    /// partir da âncora. Sem rotação os eixos são os da tela e o resultado é o
    /// mesmo de um resize alinhado comum; com rotação, é o que faz a aresta
    /// oposta ficar parada na tela em vez de girar junto.
    /// </remarks>
    public static Rectangle Resize(ResizeHandle alca, Rectangle inicio, double graus, PointF ancora, PointF mouse)
    {
        if (alca == ResizeHandle.Move)
        {
            var movida = inicio;
            movida.Offset((int)Math.Round(mouse.X - ancora.X), (int)Math.Round(mouse.Y - ancora.Y));
            return movida;
        }

        if (alca is ResizeHandle.None or ResizeHandle.Rotate) return Rectangle.Empty;

        double rad = graus * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        double rx = mouse.X - ancora.X, ry = mouse.Y - ancora.Y;
        float aoLongoX = (float)(rx * cos + ry * sin);
        float aoLongoY = (float)(-rx * sin + ry * cos);

        bool alteraX = AlteraX(alca), alteraY = AlteraY(alca);
        float largura = alteraX ? Math.Abs(aoLongoX) : inicio.Width;
        float altura = alteraY ? Math.Abs(aoLongoY) : inicio.Height;
        if (largura < MinSize || altura < MinSize) return Rectangle.Empty;

        // No eixo preservado a âncora é o meio da aresta oposta, que já está na
        // linha do centro: o deslocamento nesse eixo é zero.
        float meiaX = alteraX ? Math.Sign(aoLongoX) * largura / 2f : 0f;
        float meiaY = alteraY ? Math.Sign(aoLongoY) * altura / 2f : 0f;
        float centroX = ancora.X + (float)(meiaX * cos - meiaY * sin);
        float centroY = ancora.Y + (float)(meiaX * sin + meiaY * cos);

        return new Rectangle(
            (int)Math.Round(centroX - largura / 2f),
            (int)Math.Round(centroY - altura / 2f),
            (int)Math.Round(largura),
            (int)Math.Round(altura));
    }

    /// <summary>Mantém a seleção dentro dos limites informados.</summary>
    public static Rectangle Clamp(Rectangle r, Size bounds)
    {
        r.X = Math.Max(0, Math.Min(r.X, bounds.Width - r.Width));
        r.Y = Math.Max(0, Math.Min(r.Y, bounds.Height - r.Height));
        return r;
    }
}
