using System.Drawing;
using System.Drawing.Drawing2D;

namespace AiShot.Capture;

/// <summary>Desenho das anotações (shapes) sobre o print. Sem estado.</summary>
internal static class ShapeRenderer
{
    /// <summary>true se o shape tem tamanho suficiente para ser mantido.</summary>
    public static bool IsValid(Shape s) => s.Tool switch
    {
        Tool.Pen => s.Points is { Count: > 1 },
        _ => Math.Abs(s.A.X - s.B.X) > 2 || Math.Abs(s.A.Y - s.B.Y) > 2,
    };

    /// <summary>Desenha um shape no contexto gráfico informado.</summary>
    public static void Draw(Graphics g, Shape s)
    {
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
}
