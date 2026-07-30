using System.Drawing;

namespace AiShot.Capture;

/// <summary>
/// Estado das anotações desenhadas sobre o print: lista de formas, ferramenta
/// ativa, cor, espessura e histórico de desfazer/refazer.
/// </summary>
/// <remarks>
/// Não conhece janela nem eventos de mouse — recebe pontos e devolve estado.
/// A separação existe para que a lógica de edição possa ser verificada sem
/// abrir uma tela, e para que o overlay cuide apenas de entrada e desenho.
/// </remarks>
internal sealed class AnnotationController
{
    /// <summary>Espessuras oferecidas na barra: fina, média e grossa.</summary>
    public static readonly int[] ThicknessLevels = { 2, 4, 7 };

    /// <summary>Cores da paleta, na ordem em que aparecem.</summary>
    public static readonly Color[] Palette =
    {
        Color.FromArgb(239, 68, 68), Color.FromArgb(249, 115, 22), Color.FromArgb(234, 179, 8),
        Color.FromArgb(34, 197, 94), Color.FromArgb(59, 130, 246), Color.FromArgb(168, 85, 247),
        Color.White, Color.FromArgb(24, 24, 27),
    };

    private readonly List<Shape> _shapes = new();
    private readonly Stack<Shape> _refazer = new();
    private Shape? _emCurso;

    /// <summary>Formas já confirmadas, na ordem de desenho.</summary>
    public IReadOnlyList<Shape> Shapes => _shapes;

    /// <summary>Forma sendo arrastada no momento, ou nulo.</summary>
    public Shape? InProgress => _emCurso;

    /// <summary>Ferramenta ativa. <see cref="Tool.None"/> move a seleção.</summary>
    public Tool Tool { get; private set; } = Tool.None;

    public Color Color { get; private set; } = Palette[0];

    public int Thickness { get; private set; } = ThicknessLevels[1];

    public bool CanUndo => _shapes.Count > 0;

    public bool CanRedo => _refazer.Count > 0;

    /// <summary>
    /// Ativa a ferramenta, ou a desativa quando já era a ativa — é o
    /// comportamento de alternância dos botões da barra lateral.
    /// </summary>
    public void ToggleTool(Tool tool) => Tool = Tool == tool ? Tool.None : tool;

    public void SetColor(Color color) => Color = color;

    public void SetThickness(int thickness) => Thickness = thickness;

    // ---------- Desenho ----------

    /// <summary>Inicia uma forma no ponto informado, com a ferramenta ativa.</summary>
    public void BeginDraw(Point at)
    {
        if (Tool is Tool.None or Tool.Text) return;

        _emCurso = new Shape { Tool = Tool, Color = Color, Thickness = Thickness, A = at, B = at };
        if (Tool == Tool.Pen) _emCurso.Points = new List<Point> { at };
    }

    /// <summary>Estende a forma em curso até o ponto informado.</summary>
    public void ContinueDraw(Point to)
    {
        if (_emCurso is null) return;

        if (_emCurso.Tool == Tool.Pen) _emCurso.Points!.Add(to);
        else _emCurso.B = to;
    }

    /// <summary>
    /// Confirma a forma em curso. Descartada se degenerada — um clique sem
    /// arraste não deve virar uma anotação invisível na imagem final.
    /// </summary>
    /// <returns>true se a forma entrou na lista.</returns>
    public bool EndDraw()
    {
        if (_emCurso is null) return false;

        var forma = _emCurso;
        _emCurso = null;

        if (!ShapeRenderer.IsValid(forma)) return false;

        Add(forma);
        return true;
    }

    /// <summary>Descarta a forma em curso sem confirmá-la.</summary>
    public void CancelDraw() => _emCurso = null;

    /// <summary>Acrescenta uma forma pronta (usada pela ferramenta de texto).</summary>
    public void Add(Shape shape)
    {
        _shapes.Add(shape);
        // Uma edição nova torna o ramo desfeito inalcançável, como em qualquer
        // histórico linear de desfazer.
        _refazer.Clear();
    }

    // ---------- Histórico ----------

    /// <summary>Remove a última forma. Devolve false se não havia o que desfazer.</summary>
    public bool Undo()
    {
        if (_shapes.Count == 0) return false;

        _refazer.Push(_shapes[^1]);
        _shapes.RemoveAt(_shapes.Count - 1);
        return true;
    }

    /// <summary>Repõe a última forma desfeita. Devolve false se não havia o que refazer.</summary>
    public bool Redo()
    {
        if (_refazer.Count == 0) return false;

        _shapes.Add(_refazer.Pop());
        return true;
    }
}
