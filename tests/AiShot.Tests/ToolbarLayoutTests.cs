using System.Drawing;
using AiShot.Capture;

namespace AiShot.Tests;

/// <summary>
/// Posicionamento das toolbars em relação à seleção e ao monitor.
/// O contrato que importa é geométrico: as barras ficam dentro do monitor, não
/// se sobrepõem entre si e acompanham a seleção. As posições exatas são detalhe
/// de implementação e não são fixadas aqui.
/// </summary>
public class ToolbarLayoutTests
{
    private static readonly Rectangle Monitor = new(0, 0, 1920, 1080);

    private static ToolbarLayoutResult Calcular(Rectangle selecao, Rectangle? monitor = null) =>
        ToolbarLayout.Compute(selecao, monitor ?? Monitor, Tool.None, paletteOpen: false, thicknessOpen: false);

    private static bool DentroDe(Rectangle interno, Rectangle externo) =>
        interno.Left >= externo.Left && interno.Top >= externo.Top &&
        interno.Right <= externo.Right && interno.Bottom <= externo.Bottom;

    // ---------- Contenção no monitor ----------

    public static TheoryData<int, int, int, int> SelecoesEmPosicoesDiversas() => new()
    {
        { 800, 400, 300, 200 },     // centro
        { 0, 0, 300, 200 },         // encostada no canto superior esquerdo
        { 1620, 880, 300, 200 },    // encostada no canto inferior direito
        { 0, 440, 300, 200 },       // borda esquerda
        { 1620, 440, 300, 200 },    // borda direita
        { 800, 0, 300, 200 },       // borda superior
        { 800, 880, 300, 200 },     // borda inferior
        { 0, 0, 1920, 1080 },       // tela cheia
        { 900, 500, 20, 20 },       // seleção mínima
    };

    [Theory]
    [MemberData(nameof(SelecoesEmPosicoesDiversas))]
    public void Compute_MantemAsBarrasDentroDoMonitor(int x, int y, int w, int h)
    {
        var layout = Calcular(new Rectangle(x, y, w, h));

        Assert.True(DentroDe(layout.SidePanel, Monitor),
            $"A barra lateral {layout.SidePanel} saiu do monitor {Monitor}.");
        Assert.True(DentroDe(layout.BottomPanel, Monitor),
            $"A barra inferior {layout.BottomPanel} saiu do monitor {Monitor}.");
    }

    [Theory]
    [MemberData(nameof(SelecoesEmPosicoesDiversas))]
    public void Compute_MantemOsBotoesDentroDasSuasBarras(int x, int y, int w, int h)
    {
        var layout = Calcular(new Rectangle(x, y, w, h));

        Assert.All(layout.SideButtons, b =>
            Assert.True(DentroDe(b.Rect, layout.SidePanel),
                $"O botão '{b.Id}' {b.Rect} saiu da barra lateral {layout.SidePanel}."));
        Assert.All(layout.BottomButtons, b =>
            Assert.True(DentroDe(b.Rect, layout.BottomPanel),
                $"O botão '{b.Id}' {b.Rect} saiu da barra inferior {layout.BottomPanel}."));
    }

    [Theory]
    [MemberData(nameof(SelecoesEmPosicoesDiversas))]
    public void Compute_NaoDeixaAsBarrasSeSobreporem(int x, int y, int w, int h)
    {
        var layout = Calcular(new Rectangle(x, y, w, h));

        Assert.False(layout.SidePanel.IntersectsWith(layout.BottomPanel),
            $"A barra lateral {layout.SidePanel} colidiu com a inferior {layout.BottomPanel}.");
    }

    [Fact]
    public void Compute_ComSelecaoColadaNaBordaEsquerda_DesviaAsBarrasEmVezDeSobrepor()
    {
        // Seleção estreita encostada na esquerda: a lateral vai para a direita
        // dela e a faixa livre à esquerda fica mais estreita que a barra
        // inferior. Sem desvio vertical, o clamp horizontal traz a barra de
        // volta para cima da lateral.
        var layout = Calcular(new Rectangle(0, 440, 300, 200));

        Assert.False(layout.SidePanel.IntersectsWith(layout.BottomPanel),
            $"A barra lateral {layout.SidePanel} colidiu com a inferior {layout.BottomPanel}.");
        Assert.True(DentroDe(layout.BottomPanel, Monitor),
            $"A barra inferior {layout.BottomPanel} saiu do monitor {Monitor}.");
    }

    [Fact]
    public void Compute_ComSelecaoMaiorQueOMonitor_AindaMantemAsBarrasDentro()
    {
        var layout = Calcular(new Rectangle(-500, -500, 3000, 2000));

        Assert.True(DentroDe(layout.SidePanel, Monitor));
        Assert.True(DentroDe(layout.BottomPanel, Monitor));
    }

    [Fact]
    public void Compute_ComMonitorDeOrigemNaoZero_PosicionaRelativoAEle()
    {
        // Monitor secundário à direita do principal, na área virtual.
        var secundario = new Rectangle(1920, 0, 1280, 720);

        var layout = Calcular(new Rectangle(2200, 200, 400, 300), secundario);

        Assert.True(DentroDe(layout.SidePanel, secundario));
        Assert.True(DentroDe(layout.BottomPanel, secundario));
    }

    // ---------- Botões ----------

    [Fact]
    public void Compute_ProduzOsBotoesEsperadosNaBarraLateral()
    {
        var layout = Calcular(new Rectangle(800, 400, 300, 200));

        Assert.Equal(
            new[] { "pen", "arrow", "line", "rect", "ellipse", "text", "blur", "step", "color", "thickness", "undo", "redo" },
            layout.SideButtons.Select(b => b.Id));
    }

    [Fact]
    public void Compute_ProduzOsBotoesEsperadosNaBarraInferior()
    {
        var layout = Calcular(new Rectangle(800, 400, 300, 200));

        Assert.Equal(
            new[] { "copy", "ocr", "save", "paint", "upload", "share", "ai", "close" },
            layout.BottomButtons.Select(b => b.Id));
    }

    [Fact]
    public void Compute_TodoBotaoTemNomeParaOLeitorDeTela()
    {
        // A dica é o que a árvore de acessibilidade anuncia (ver
        // OverlayAccessibility). Um botão sem dica seria anunciado sem nome,
        // e quem depende do Narrador não saberia o que está focando.
        var layout = Calcular(new Rectangle(800, 400, 300, 200));

        Assert.All(layout.SideButtons.Concat(layout.BottomButtons), b =>
            Assert.False(string.IsNullOrWhiteSpace(b.Tip),
                $"O botão '{b.Id}' não tem nome para o leitor de tela."));
    }

    [Fact]
    public void Compute_NaoRepeteIdentificadoresDeBotao()
    {
        var layout = Calcular(new Rectangle(800, 400, 300, 200));

        var todos = layout.SideButtons.Concat(layout.BottomButtons).Select(b => b.Id).ToArray();

        Assert.Equal(todos.Length, todos.Distinct().Count());
    }

    [Fact]
    public void Compute_DaDicaDeFerramentaATodosOsBotoes()
    {
        var layout = Calcular(new Rectangle(800, 400, 300, 200));

        Assert.All(layout.SideButtons.Concat(layout.BottomButtons),
            b => Assert.False(string.IsNullOrWhiteSpace(b.Tip), $"O botão '{b.Id}' está sem dica."));
    }

    [Fact]
    public void Compute_NaoDeixaOsBotoesSeSobreporem()
    {
        var layout = Calcular(new Rectangle(800, 400, 300, 200));

        foreach (var grupo in new[] { layout.SideButtons, layout.BottomButtons })
            for (int i = 0; i < grupo.Count; i++)
                for (int j = i + 1; j < grupo.Count; j++)
                    Assert.False(grupo[i].Rect.IntersectsWith(grupo[j].Rect),
                        $"Os botões '{grupo[i].Id}' e '{grupo[j].Id}' se sobrepõem.");
    }

    // ---------- Estado ativo ----------

    [Theory]
    [InlineData("Pen", "pen")]
    [InlineData("Arrow", "arrow")]
    [InlineData("Line", "line")]
    [InlineData("Rect", "rect")]
    [InlineData("Ellipse", "ellipse")]
    [InlineData("Text", "text")]
    [InlineData("Blur", "blur")]
    [InlineData("Step", "step")]
    public void Compute_MarcaComoAtivoApenasOBotaoDaFerramentaEmUso(string ferramenta, string idEsperado)
    {
        var layout = ToolbarLayout.Compute(
            new Rectangle(800, 400, 300, 200), Monitor,
            Enum.Parse<Tool>(ferramenta), paletteOpen: false, thicknessOpen: false);

        var ativos = layout.SideButtons.Where(b => b.Active).Select(b => b.Id).ToArray();

        Assert.Equal(new[] { idEsperado }, ativos);
    }

    [Fact]
    public void Compute_ComFerramentaNone_NaoMarcaNenhumBotaoDeDesenho()
    {
        var layout = Calcular(new Rectangle(800, 400, 300, 200));

        Assert.DoesNotContain(layout.SideButtons, b => b.Active);
    }

    [Fact]
    public void Compute_ComPaletaAberta_MarcaApenasOBotaoDeCor()
    {
        var layout = ToolbarLayout.Compute(
            new Rectangle(800, 400, 300, 200), Monitor,
            Tool.None, paletteOpen: true, thicknessOpen: false);

        Assert.Equal(new[] { "color" }, layout.SideButtons.Where(b => b.Active).Select(b => b.Id));
    }

    [Fact]
    public void Compute_ComMenuDeEspessuraAberto_MarcaApenasOBotaoDeEspessura()
    {
        var layout = ToolbarLayout.Compute(
            new Rectangle(800, 400, 300, 200), Monitor,
            Tool.None, paletteOpen: false, thicknessOpen: true);

        Assert.Equal(new[] { "thickness" }, layout.SideButtons.Where(b => b.Active).Select(b => b.Id));
    }

    [Fact]
    public void Compute_NaoMarcaBotoesDaBarraInferiorComoAtivos()
    {
        var layout = ToolbarLayout.Compute(
            new Rectangle(800, 400, 300, 200), Monitor,
            Tool.Pen, paletteOpen: true, thicknessOpen: true);

        Assert.DoesNotContain(layout.BottomButtons, b => b.Active);
    }
}
