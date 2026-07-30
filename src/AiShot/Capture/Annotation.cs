using System.Drawing;

namespace AiShot.Capture;

/// <summary>Ferramenta de anotação ativa.</summary>
internal enum Tool { None, Pen, Arrow, Line, Rect, Ellipse, Text, Blur, Step }

/// <summary>Alça de redimensionamento/movimento da seleção.</summary>
internal enum ResizeHandle { None, TL, T, TR, R, BR, B, BL, L, Move }

/// <summary>Uma anotação vetorial desenhada sobre o print.</summary>
internal sealed class Shape
{
    public Tool Tool;
    public Color Color;
    public int Thickness;
    public Point A, B;
    public List<Point>? Points;   // pen (traço livre)
    public string? TextValue;     // texto
    public int StepNumber;        // numeração de passos: o número exibido
}

/// <summary>Botão de ícone numa toolbar (calculado por frame).</summary>
internal sealed record IconButton(Rectangle Rect, string Glyph, string Id, bool Active, string Tip);
