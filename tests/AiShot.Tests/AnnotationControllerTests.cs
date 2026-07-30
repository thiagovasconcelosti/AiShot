using System.Drawing;
using AiShot.Capture;

namespace AiShot.Tests;

/// <summary>
/// Estado das anotações: ferramenta ativa, desenho em curso e histórico de
/// desfazer/refazer.
/// </summary>
public class AnnotationControllerTests
{
    private static AnnotationController Novo() => new();

    /// <summary>Desenha uma forma válida (arraste com extensão suficiente).</summary>
    private static void Desenhar(AnnotationController c, Tool ferramenta = Tool.Rect)
    {
        c.ToggleTool(ferramenta);
        c.BeginDraw(new Point(10, 10));
        c.ContinueDraw(new Point(120, 90));
        c.EndDraw();
        c.ToggleTool(ferramenta); // desativa, para não interferir no próximo passo
    }

    // ---------- Ferramenta ativa ----------

    [Fact]
    public void Inicia_SemFerramentaAtiva() =>
        Assert.Equal(Tool.None, Novo().Tool);

    [Fact]
    public void ToggleTool_AtivaAFerramenta()
    {
        var c = Novo();

        c.ToggleTool(Tool.Arrow);

        Assert.Equal(Tool.Arrow, c.Tool);
    }

    [Fact]
    public void ToggleTool_ChamadoDuasVezes_Desativa()
    {
        var c = Novo();

        c.ToggleTool(Tool.Arrow);
        c.ToggleTool(Tool.Arrow);

        Assert.Equal(Tool.None, c.Tool);
    }

    [Fact]
    public void ToggleTool_ComOutraFerramenta_Troca()
    {
        var c = Novo();

        c.ToggleTool(Tool.Arrow);
        c.ToggleTool(Tool.Ellipse);

        Assert.Equal(Tool.Ellipse, c.Tool);
    }

    // ---------- Cor e espessura ----------

    [Fact]
    public void Inicia_ComAPrimeiraCorDaPaletaEEspessuraMedia()
    {
        var c = Novo();

        Assert.Equal(AnnotationController.Palette[0], c.Color);
        Assert.Equal(AnnotationController.ThicknessLevels[1], c.Thickness);
    }

    [Fact]
    public void FormaNova_HerdaACorEAEspessuraAtuais()
    {
        var c = Novo();
        c.SetColor(Color.Lime);
        c.SetThickness(7);
        c.ToggleTool(Tool.Rect);

        c.BeginDraw(new Point(0, 0));
        c.ContinueDraw(new Point(100, 100));
        c.EndDraw();

        var forma = Assert.Single(c.Shapes);
        Assert.Equal(Color.Lime, forma.Color);
        Assert.Equal(7, forma.Thickness);
    }

    [Fact]
    public void MudarACor_NaoAlteraAsFormasJaDesenhadas()
    {
        var c = Novo();
        c.SetColor(Color.Red);
        Desenhar(c);

        c.SetColor(Color.Blue);

        Assert.Equal(Color.Red, c.Shapes[0].Color);
    }

    // ---------- Desenho ----------

    [Fact]
    public void BeginDraw_SemFerramenta_NaoIniciaNada()
    {
        var c = Novo();

        c.BeginDraw(new Point(10, 10));

        Assert.Null(c.InProgress);
    }

    [Fact]
    public void BeginDraw_ComFerramentaDeTexto_NaoIniciaArraste()
    {
        // O texto é inserido por um campo de edição, não por arraste.
        var c = Novo();
        c.ToggleTool(Tool.Text);

        c.BeginDraw(new Point(10, 10));

        Assert.Null(c.InProgress);
    }

    [Fact]
    public void ContinueDraw_ComLapis_AcumulaOsPontos()
    {
        var c = Novo();
        c.ToggleTool(Tool.Pen);

        c.BeginDraw(new Point(0, 0));
        c.ContinueDraw(new Point(5, 5));
        c.ContinueDraw(new Point(10, 10));

        Assert.Equal(3, c.InProgress!.Points!.Count); // o ponto inicial conta
    }

    [Fact]
    public void ContinueDraw_ComRetangulo_MoveApenasOSegundoCanto()
    {
        var c = Novo();
        c.ToggleTool(Tool.Rect);

        c.BeginDraw(new Point(10, 10));
        c.ContinueDraw(new Point(50, 60));

        Assert.Equal(new Point(10, 10), c.InProgress!.A);
        Assert.Equal(new Point(50, 60), c.InProgress.B);
    }

    [Fact]
    public void EndDraw_ComFormaValida_ConfirmaEAcrescenta()
    {
        var c = Novo();
        c.ToggleTool(Tool.Rect);
        c.BeginDraw(new Point(0, 0));
        c.ContinueDraw(new Point(100, 100));

        Assert.True(c.EndDraw());
        Assert.Single(c.Shapes);
        Assert.Null(c.InProgress);
    }

    [Fact]
    public void EndDraw_ComCliqueSemArraste_Descarta()
    {
        // Um clique sem movimento não deve virar anotação invisível na imagem.
        var c = Novo();
        c.ToggleTool(Tool.Rect);
        c.BeginDraw(new Point(10, 10));

        Assert.False(c.EndDraw());
        Assert.Empty(c.Shapes);
    }

    [Fact]
    public void EndDraw_SemDesenhoEmCurso_NaoLanca()
    {
        var c = Novo();

        Assert.False(c.EndDraw());
    }

    [Fact]
    public void CancelDraw_DescartaODesenhoEmCurso()
    {
        var c = Novo();
        c.ToggleTool(Tool.Rect);
        c.BeginDraw(new Point(0, 0));
        c.ContinueDraw(new Point(100, 100));

        c.CancelDraw();

        Assert.Null(c.InProgress);
        Assert.Empty(c.Shapes);
    }

    // ---------- Desfazer ----------

    [Fact]
    public void Undo_SemFormas_DevolveFalso() =>
        Assert.False(Novo().Undo());

    [Fact]
    public void Undo_RemoveAUltimaForma()
    {
        var c = Novo();
        Desenhar(c);
        Desenhar(c);

        Assert.True(c.Undo());
        Assert.Single(c.Shapes);
    }

    [Fact]
    public void Undo_RepetidoAteEsvaziar_ParaDeDevolverVerdadeiro()
    {
        var c = Novo();
        Desenhar(c);

        Assert.True(c.Undo());
        Assert.False(c.Undo());
        Assert.Empty(c.Shapes);
    }

    [Fact]
    public void CanUndo_RefleteAExistenciaDeFormas()
    {
        var c = Novo();
        Assert.False(c.CanUndo);

        Desenhar(c);
        Assert.True(c.CanUndo);

        c.Undo();
        Assert.False(c.CanUndo);
    }

    // ---------- Refazer ----------

    [Fact]
    public void Redo_SemNadaDesfeito_DevolveFalso() =>
        Assert.False(Novo().Redo());

    [Fact]
    public void Redo_ReponAUltimaFormaDesfeita()
    {
        var c = Novo();
        Desenhar(c);
        var original = c.Shapes[0];
        c.Undo();

        Assert.True(c.Redo());
        Assert.Same(original, Assert.Single(c.Shapes));
    }

    [Fact]
    public void Redo_PreservaAOrdemDeVariasFormas()
    {
        var c = Novo();
        Desenhar(c, Tool.Rect);
        Desenhar(c, Tool.Ellipse);
        var esperado = c.Shapes.ToArray();

        c.Undo();
        c.Undo();
        c.Redo();
        c.Redo();

        Assert.Equal(esperado, c.Shapes);
    }

    [Fact]
    public void DesenharDepoisDeDesfazer_TornaORefazerInalcancavel()
    {
        // Histórico linear: o ramo desfeito é abandonado quando surge uma
        // edição nova, como em qualquer editor.
        var c = Novo();
        Desenhar(c);
        c.Undo();

        Desenhar(c);

        Assert.False(c.CanRedo);
        Assert.Single(c.Shapes);
    }

    [Fact]
    public void CanRedo_RefleteOHistoricoDesfeito()
    {
        var c = Novo();
        Assert.False(c.CanRedo);

        Desenhar(c);
        Assert.False(c.CanRedo);

        c.Undo();
        Assert.True(c.CanRedo);

        c.Redo();
        Assert.False(c.CanRedo);
    }

    // ---------- Formas prontas ----------

    [Fact]
    public void Add_AcrescentaAFormaEZeraORefazer()
    {
        var c = Novo();
        Desenhar(c);
        c.Undo();

        c.Add(new Shape { Tool = Tool.Text, TextValue = "olá", A = new Point(5, 5) });

        Assert.Single(c.Shapes);
        Assert.False(c.CanRedo);
    }
}
