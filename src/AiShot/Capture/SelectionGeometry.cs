using System.Drawing;

namespace AiShot.Capture;

/// <summary>
/// Geometria pura da seleção (sem estado/UI) — funções testáveis sobre
/// <see cref="Rectangle"/>. Usada pelo overlay para hit-test e resize/move.
/// </summary>
internal static class SelectionGeometry
{
    public const int MinSize = 16;
    private const int HandleSize = 9;

    /// <summary>Retângulo normalizado entre dois pontos (cantos em qualquer ordem).</summary>
    public static Rectangle Normalize(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    /// <summary>As 8 alças (cantos + meios) de uma seleção, na ordem TL,T,TR,R,BR,B,BL,L.</summary>
    public static Rectangle[] HandleRects(Rectangle r)
    {
        int h = HandleSize / 2;
        Point[] pts =
        {
            new(r.Left, r.Top), new(r.Left + r.Width / 2, r.Top), new(r.Right, r.Top),
            new(r.Right, r.Top + r.Height / 2), new(r.Right, r.Bottom),
            new(r.Left + r.Width / 2, r.Bottom), new(r.Left, r.Bottom), new(r.Left, r.Top + r.Height / 2),
        };
        return pts.Select(p => new Rectangle(p.X - h, p.Y - h, HandleSize, HandleSize)).ToArray();
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

    /// <summary>
    /// Aplica resize/move a partir da alça e do delta do arraste. Retorna o novo
    /// retângulo, ou <paramref name="current"/> se ficar menor que o mínimo.
    /// </summary>
    public static Rectangle ResizeOrMove(ResizeHandle handle, Rectangle start, Point dragStart, Point now, Rectangle current)
    {
        int dx = now.X - dragStart.X, dy = now.Y - dragStart.Y;
        var r = start;
        switch (handle)
        {
            case ResizeHandle.Move: r.Offset(dx, dy); break;
            case ResizeHandle.TL: r = Rectangle.FromLTRB(r.Left + dx, r.Top + dy, r.Right, r.Bottom); break;
            case ResizeHandle.T: r = Rectangle.FromLTRB(r.Left, r.Top + dy, r.Right, r.Bottom); break;
            case ResizeHandle.TR: r = Rectangle.FromLTRB(r.Left, r.Top + dy, r.Right + dx, r.Bottom); break;
            case ResizeHandle.R: r = Rectangle.FromLTRB(r.Left, r.Top, r.Right + dx, r.Bottom); break;
            case ResizeHandle.BR: r = Rectangle.FromLTRB(r.Left, r.Top, r.Right + dx, r.Bottom + dy); break;
            case ResizeHandle.B: r = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom + dy); break;
            case ResizeHandle.BL: r = Rectangle.FromLTRB(r.Left + dx, r.Top, r.Right, r.Bottom + dy); break;
            case ResizeHandle.L: r = Rectangle.FromLTRB(r.Left + dx, r.Top, r.Right, r.Bottom); break;
        }
        return r.Width >= MinSize && r.Height >= MinSize ? r : current;
    }

    /// <summary>Mantém a seleção dentro dos limites informados.</summary>
    public static Rectangle Clamp(Rectangle r, Size bounds)
    {
        r.X = Math.Max(0, Math.Min(r.X, bounds.Width - r.Width));
        r.Y = Math.Max(0, Math.Min(r.Y, bounds.Height - r.Height));
        return r;
    }
}
