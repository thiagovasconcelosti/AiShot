using System.Drawing.Drawing2D;

namespace AiShot.UI;

/// <summary>
/// Paleta e helpers visuais — re-leitura "shadcn/Geist" dark: superfícies quase
/// pretas, bordas sutis (zinc), texto branco/mutado, cantos arredondados.
/// </summary>
public static class Theme
{
    // Superfícies
    public static readonly Color Surface = Color.FromArgb(245, 9, 9, 11);      // zinc-950 quase opaco
    public static readonly Color SurfaceHover = Color.FromArgb(255, 39, 39, 42); // zinc-800
    public static readonly Color Border = Color.FromArgb(255, 63, 63, 70);      // zinc-700
    public static readonly Color BorderSubtle = Color.FromArgb(255, 39, 39, 42);
    public static readonly Color InputBackground = Color.FromArgb(24, 24, 27);  // zinc-950 (campo de texto)

    // Texto
    public static readonly Color Text = Color.FromArgb(255, 250, 250, 250);
    public static readonly Color TextMuted = Color.FromArgb(255, 161, 161, 170); // zinc-400
    public static readonly Color Accent = Color.White;

    // Seleção
    public static readonly Color SelectionStroke = Color.White;
    public static readonly Color Dim = Color.FromArgb(140, 0, 0, 0); // escurece o resto

    /// <summary>Contorno do botão com foco do teclado.</summary>
    /// <remarks>
    /// Branco puro sobre as superfícies escuras do tema: é o maior contraste
    /// disponível na paleta, e o indicador de foco precisa ser percebido antes
    /// de qualquer outra coisa na barra.
    /// </remarks>
    public static readonly Color FocusRing = Color.White;

    public const int Radius = 10;
    public const int ButtonSize = 38;
    public const int BarPad = 6;

    /// <summary>
    /// Razão de contraste entre duas cores, conforme a WCAG (Web Content
    /// Accessibility Guidelines). Vai de 1 (idênticas) a 21 (preto sobre
    /// branco).
    /// </summary>
    /// <remarks>
    /// As cores precisam ser opacas. A composição de uma cor com transparência
    /// depende do que está atrás dela, então é responsabilidade de quem chama
    /// achatar a cor sobre o fundo real antes de medir.
    /// </remarks>
    public static double ContrastRatio(Color primeira, Color segunda)
    {
        double a = LuminanciaRelativa(primeira);
        double b = LuminanciaRelativa(segunda);
        (double clara, double escura) = a >= b ? (a, b) : (b, a);
        return (clara + 0.05) / (escura + 0.05);
    }

    /// <summary>
    /// Achata uma cor com transparência sobre um fundo opaco, devolvendo a cor
    /// que o olho de fato enxerga.
    /// </summary>
    public static Color Achatar(Color frente, Color fundo)
    {
        double alfa = frente.A / 255.0;
        return Color.FromArgb(
            255,
            (int)Math.Round(frente.R * alfa + fundo.R * (1 - alfa)),
            (int)Math.Round(frente.G * alfa + fundo.G * (1 - alfa)),
            (int)Math.Round(frente.B * alfa + fundo.B * (1 - alfa)));
    }

    /// <summary>Luminância relativa da WCAG (0 = preto, 1 = branco).</summary>
    private static double LuminanciaRelativa(Color c) =>
        0.2126 * CanalLinear(c.R) + 0.7152 * CanalLinear(c.G) + 0.0722 * CanalLinear(c.B);

    /// <summary>Remove a correção gama de um canal, levando-o ao espaço linear.</summary>
    private static double CanalLinear(byte valor)
    {
        double v = valor / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    /// <summary>Cria um caminho de retângulo arredondado.</summary>
    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        if (radius <= 0) { p.AddRectangle(r); p.CloseFigure(); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Desenha um painel (barra) escuro com borda sutil.</summary>
    public static void DrawPanel(Graphics g, Rectangle r)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundRect(r, Radius);
        using var fill = new SolidBrush(Surface);
        using var pen = new Pen(BorderSubtle, 1);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }
}
