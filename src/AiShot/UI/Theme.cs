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

    // Texto
    public static readonly Color Text = Color.FromArgb(255, 250, 250, 250);
    public static readonly Color TextMuted = Color.FromArgb(255, 161, 161, 170); // zinc-400
    public static readonly Color Accent = Color.White;

    // Seleção
    public static readonly Color SelectionStroke = Color.White;
    public static readonly Color Dim = Color.FromArgb(140, 0, 0, 0); // escurece o resto

    public const int Radius = 10;
    public const int ButtonSize = 38;
    public const int BarPad = 6;

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

    /// <summary>Desenha um painel (barra) escuro com borda sutil e sombra leve.</summary>
    public static void DrawPanel(Graphics g, Rectangle r)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // sombra
        using (var shadow = new GraphicsPath())
        {
            var sr = r; sr.Offset(0, 3);
            using var sp = RoundRect(sr, Radius);
            using var sb = new PathGradientBrush(sp) { CenterColor = Color.FromArgb(70, 0, 0, 0), SurroundColors = new[] { Color.Transparent } };
        }
        using var path = RoundRect(r, Radius);
        using var fill = new SolidBrush(Surface);
        using var pen = new Pen(BorderSubtle, 1);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }
}
