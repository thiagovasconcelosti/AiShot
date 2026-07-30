using System.Drawing;
using AiShot.Capture;

namespace AiShot.Tests;

/// <summary>
/// Geometria da seleção: normalização, alças, redimensionamento e contenção.
/// São funções puras — nada aqui depende de janela ou de estado do overlay.
/// </summary>
/// <remarks>
/// O xUnit exige classes de teste públicas (xUnit1000), mas <see cref="ResizeHandle"/>
/// é <c>internal</c> e não pode aparecer na assinatura de um método público
/// (CS0051). Os casos parametrizados por alça passam o nome do valor como texto
/// e convertem dentro do corpo do teste.
/// </remarks>
public class SelectionGeometryTests
{
    // ---------- Normalize ----------

    [Fact]
    public void Normalize_ComPontosEmOrdem_ProduzRetanguloEquivalente()
    {
        var r = SelectionGeometry.Normalize(new Point(10, 20), new Point(110, 220));

        Assert.Equal(new Rectangle(10, 20, 100, 200), r);
    }

    [Theory]
    [InlineData(110, 220, 10, 20)]   // arrasto da direita para a esquerda, de baixo para cima
    [InlineData(10, 220, 110, 20)]   // apenas o eixo vertical invertido
    [InlineData(110, 20, 10, 220)]   // apenas o eixo horizontal invertido
    public void Normalize_ComPontosInvertidos_ProduzOMesmoRetangulo(int ax, int ay, int bx, int by)
    {
        var r = SelectionGeometry.Normalize(new Point(ax, ay), new Point(bx, by));

        Assert.Equal(new Rectangle(10, 20, 100, 200), r);
    }

    [Fact]
    public void Normalize_ComPontosIguais_ProduzRetanguloVazio()
    {
        var r = SelectionGeometry.Normalize(new Point(50, 50), new Point(50, 50));

        Assert.Equal(0, r.Width);
        Assert.Equal(0, r.Height);
    }

    [Fact]
    public void Normalize_ComCoordenadasNegativas_PreservaOSinal()
    {
        // A área virtual de múltiplos monitores pode ter origem negativa.
        var r = SelectionGeometry.Normalize(new Point(-100, -50), new Point(-20, -10));

        Assert.Equal(Rectangle.FromLTRB(-100, -50, -20, -10), r);
    }

    // ---------- HandleRects ----------

    [Fact]
    public void HandleRects_SempreProduzOitoAlcas()
    {
        Assert.Equal(8, SelectionGeometry.HandleRects(new Rectangle(0, 0, 100, 100)).Length);
    }

    [Fact]
    public void HandleRects_CentraAsAlcasNosCantosENosMeios()
    {
        var sel = new Rectangle(100, 100, 200, 100);

        var rects = SelectionGeometry.HandleRects(sel);

        // Ordem documentada: TL, T, TR, R, BR, B, BL, L.
        Point[] esperados =
        {
            new(sel.Left, sel.Top),
            new(sel.Left + sel.Width / 2, sel.Top),
            new(sel.Right, sel.Top),
            new(sel.Right, sel.Top + sel.Height / 2),
            new(sel.Right, sel.Bottom),
            new(sel.Left + sel.Width / 2, sel.Bottom),
            new(sel.Left, sel.Bottom),
            new(sel.Left, sel.Top + sel.Height / 2),
        };

        for (int i = 0; i < esperados.Length; i++)
        {
            var centro = new Point(
                rects[i].Left + rects[i].Width / 2,
                rects[i].Top + rects[i].Height / 2);
            Assert.Equal(esperados[i], centro);
        }
    }

    // ---------- HitHandle ----------

    [Theory]
    [InlineData("TL")]
    [InlineData("T")]
    [InlineData("TR")]
    [InlineData("R")]
    [InlineData("BR")]
    [InlineData("B")]
    [InlineData("BL")]
    [InlineData("L")]
    public void HitHandle_NoCentroDeCadaAlca_DevolveAAlcaCorrespondente(string nomeDaAlca)
    {
        var esperada = Enum.Parse<ResizeHandle>(nomeDaAlca);
        var sel = new Rectangle(100, 100, 200, 100);
        ResizeHandle[] ordem =
        {
            ResizeHandle.TL, ResizeHandle.T, ResizeHandle.TR, ResizeHandle.R,
            ResizeHandle.BR, ResizeHandle.B, ResizeHandle.BL, ResizeHandle.L,
        };
        int indice = Array.IndexOf(ordem, esperada);
        var alca = SelectionGeometry.HandleRects(sel)[indice];
        var centro = new Point(alca.Left + alca.Width / 2, alca.Top + alca.Height / 2);

        Assert.Equal(esperada, SelectionGeometry.HitHandle(sel, centro));
    }

    [Fact]
    public void HitHandle_NoMeioDaSelecao_NaoDevolveAlca()
    {
        var sel = new Rectangle(100, 100, 200, 100);

        Assert.Equal(ResizeHandle.None, SelectionGeometry.HitHandle(sel, new Point(200, 150)));
    }

    [Fact]
    public void HitHandle_LongeDaSelecao_NaoDevolveAlca()
    {
        var sel = new Rectangle(100, 100, 200, 100);

        Assert.Equal(ResizeHandle.None, SelectionGeometry.HitHandle(sel, new Point(9999, 9999)));
    }

    // ---------- ResizeOrMove ----------

    [Fact]
    public void ResizeOrMove_ComMove_DeslocaSemAlterarOTamanho()
    {
        var inicial = new Rectangle(100, 100, 200, 100);

        var r = SelectionGeometry.ResizeOrMove(
            ResizeHandle.Move, inicial, new Point(150, 150), new Point(170, 130), inicial);

        Assert.Equal(new Rectangle(120, 80, 200, 100), r);
    }

    [Fact]
    public void ResizeOrMove_ComBR_MoveApenasOCantoInferiorDireito()
    {
        var inicial = new Rectangle(100, 100, 200, 100);

        var r = SelectionGeometry.ResizeOrMove(
            ResizeHandle.BR, inicial, new Point(300, 200), new Point(350, 260), inicial);

        Assert.Equal(new Rectangle(100, 100, 250, 160), r);
    }

    [Fact]
    public void ResizeOrMove_ComTL_MoveApenasOCantoSuperiorEsquerdo()
    {
        var inicial = new Rectangle(100, 100, 200, 100);

        var r = SelectionGeometry.ResizeOrMove(
            ResizeHandle.TL, inicial, new Point(100, 100), new Point(120, 130), inicial);

        Assert.Equal(Rectangle.FromLTRB(120, 130, 300, 200), r);
    }

    [Theory]
    [InlineData("T")]
    [InlineData("B")]
    public void ResizeOrMove_ComAlcaVertical_PreservaALargura(string nomeDaAlca)
    {
        var inicial = new Rectangle(100, 100, 200, 100);

        var r = SelectionGeometry.ResizeOrMove(
            Enum.Parse<ResizeHandle>(nomeDaAlca), inicial,
            new Point(200, 100), new Point(240, 130), inicial);

        Assert.Equal(inicial.Width, r.Width);
    }

    [Theory]
    [InlineData("L")]
    [InlineData("R")]
    public void ResizeOrMove_ComAlcaHorizontal_PreservaAAltura(string nomeDaAlca)
    {
        var inicial = new Rectangle(100, 100, 200, 100);

        var r = SelectionGeometry.ResizeOrMove(
            Enum.Parse<ResizeHandle>(nomeDaAlca), inicial,
            new Point(100, 150), new Point(130, 190), inicial);

        Assert.Equal(inicial.Height, r.Height);
    }

    [Fact]
    public void ResizeOrMove_QuandoOResultadoFicaMenorQueOMinimo_DevolveOAtual()
    {
        var inicial = new Rectangle(100, 100, 200, 100);
        var atual = new Rectangle(1, 2, 3, 4); // sentinela: identifica o retorno

        // Arrasta o canto inferior direito para muito perto do superior esquerdo.
        var r = SelectionGeometry.ResizeOrMove(
            ResizeHandle.BR, inicial, new Point(300, 200), new Point(105, 105), atual);

        Assert.Equal(atual, r);
    }

    [Fact]
    public void ResizeOrMove_ExatamenteNoTamanhoMinimo_AceitaOResultado()
    {
        var inicial = new Rectangle(100, 100, 200, 100);
        var atual = new Rectangle(1, 2, 3, 4);

        // Reduz para exatamente MinSize em ambos os eixos — o limite é inclusivo.
        int dx = SelectionGeometry.MinSize - inicial.Width;
        int dy = SelectionGeometry.MinSize - inicial.Height;
        var r = SelectionGeometry.ResizeOrMove(
            ResizeHandle.BR, inicial, new Point(300, 200), new Point(300 + dx, 200 + dy), atual);

        Assert.Equal(SelectionGeometry.MinSize, r.Width);
        Assert.Equal(SelectionGeometry.MinSize, r.Height);
    }

    [Fact]
    public void ResizeOrMove_ComNone_NaoAlteraORetangulo()
    {
        var inicial = new Rectangle(100, 100, 200, 100);

        var r = SelectionGeometry.ResizeOrMove(
            ResizeHandle.None, inicial, new Point(0, 0), new Point(50, 50), inicial);

        Assert.Equal(inicial, r);
    }

    // ---------- Clamp ----------

    [Fact]
    public void Clamp_ComSelecaoJaDentro_NaoAltera()
    {
        var r = new Rectangle(10, 10, 100, 100);

        Assert.Equal(r, SelectionGeometry.Clamp(r, new Size(800, 600)));
    }

    [Fact]
    public void Clamp_ComSelecaoAlemDaBordaInferiorDireita_TrazParaDentro()
    {
        var r = SelectionGeometry.Clamp(new Rectangle(750, 550, 100, 100), new Size(800, 600));

        Assert.Equal(new Rectangle(700, 500, 100, 100), r);
    }

    [Fact]
    public void Clamp_ComOrigemNegativa_TrazParaAOrigem()
    {
        var r = SelectionGeometry.Clamp(new Rectangle(-50, -30, 100, 100), new Size(800, 600));

        Assert.Equal(new Rectangle(0, 0, 100, 100), r);
    }

    [Fact]
    public void Clamp_ComSelecaoMaiorQueOsLimites_AncoraNaOrigem()
    {
        // Não há posição válida: o mínimo da coordenada vence o máximo negativo.
        var r = SelectionGeometry.Clamp(new Rectangle(10, 10, 1000, 900), new Size(800, 600));

        Assert.Equal(0, r.X);
        Assert.Equal(0, r.Y);
    }

    [Fact]
    public void Clamp_PreservaAsDimensoes()
    {
        var r = SelectionGeometry.Clamp(new Rectangle(-500, 9999, 123, 456), new Size(800, 600));

        Assert.Equal(123, r.Width);
        Assert.Equal(456, r.Height);
    }
}
