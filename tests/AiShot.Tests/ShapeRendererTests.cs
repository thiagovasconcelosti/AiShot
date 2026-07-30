using System.Drawing;
using System.Drawing.Imaging;
using AiShot.Capture;

namespace AiShot.Tests;

/// <summary>
/// Desenho das anotações, com atenção ao borrão: a ferramenta existe para
/// ocultar dados sensíveis antes de compartilhar, então o teste central é que o
/// conteúdo original não sobreviva na imagem produzida.
/// </summary>
public class ShapeRendererTests
{
    /// <summary>Imagem com um padrão de alta frequência, fácil de reconhecer.</summary>
    private static Bitmap CriarOrigem(int largura = 120, int altura = 120)
    {
        var bmp = new Bitmap(largura, altura, PixelFormat.Format32bppArgb);
        for (int y = 0; y < altura; y++)
            for (int x = 0; x < largura; x++)
                // Xadrez de um pixel: preto e branco alternados.
                bmp.SetPixel(x, y, (x + y) % 2 == 0 ? Color.Black : Color.White);
        return bmp;
    }

    private static Bitmap Renderizar(Shape forma, Bitmap origem)
    {
        var destino = new Bitmap(origem.Width, origem.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(destino);
        g.DrawImage(origem, Point.Empty);
        ShapeRenderer.Draw(g, forma, origem);
        return destino;
    }

    private static Shape Borrao(Rectangle area) => new()
    {
        Tool = Tool.Blur,
        A = area.Location,
        B = new Point(area.Right, area.Bottom),
    };

    // ---------- Borrão: o conteúdo original não pode sobreviver ----------

    [Fact]
    public void Borrao_TornaOsPixeisDaAreaUniformesEmBlocos()
    {
        using var origem = CriarOrigem();
        var area = new Rectangle(24, 24, 48, 48);

        using var resultado = Renderizar(Borrao(area), origem);

        // No xadrez original, dois pixels vizinhos nunca são iguais. Depois da
        // pixelização, o interior de cada bloco passa a ser de cor única.
        int vizinhosIguais = 0, total = 0;
        for (int y = area.Top + 2; y < area.Bottom - 2; y++)
            for (int x = area.Left + 2; x < area.Right - 3; x++)
            {
                total++;
                if (resultado.GetPixel(x, y).ToArgb() == resultado.GetPixel(x + 1, y).ToArgb())
                    vizinhosIguais++;
            }

        Assert.True(vizinhosIguais > total * 0.5,
            $"A área deveria ter virado blocos de cor chapada; apenas {vizinhosIguais}/{total} vizinhos ficaram iguais.");
    }

    [Fact]
    public void Borrao_NaoPreservaOPadraoOriginalDentroDaArea()
    {
        using var origem = CriarOrigem();
        var area = new Rectangle(30, 30, 40, 40);

        using var resultado = Renderizar(Borrao(area), origem);

        // Se a ferramenta fosse uma camada translúcida, a alternância preto e
        // branco continuaria detectável — o dado seria recuperável.
        int iguaisAoOriginal = 0, total = 0;
        for (int y = area.Top + 2; y < area.Bottom - 2; y++)
            for (int x = area.Left + 2; x < area.Right - 2; x++)
            {
                total++;
                if (resultado.GetPixel(x, y).ToArgb() == origem.GetPixel(x, y).ToArgb())
                    iguaisAoOriginal++;
            }

        Assert.True(iguaisAoOriginal < total * 0.6,
            $"Boa parte dos pixels originais sobreviveu ({iguaisAoOriginal}/{total}) — o borrão não está ocultando o conteúdo.");
    }

    [Fact]
    public void Borrao_NaoAlteraOQueEstaForaDaArea()
    {
        using var origem = CriarOrigem();
        var area = new Rectangle(40, 40, 30, 30);

        using var resultado = Renderizar(Borrao(area), origem);

        // Um ponto claramente fora, longe da borda.
        Assert.Equal(origem.GetPixel(5, 5).ToArgb(), resultado.GetPixel(5, 5).ToArgb());
        Assert.Equal(origem.GetPixel(110, 110).ToArgb(), resultado.GetPixel(110, 110).ToArgb());
    }

    [Fact]
    public void Borrao_ComAreaAlemDaBorda_NaoLanca()
    {
        using var origem = CriarOrigem(60, 60);
        var forma = Borrao(new Rectangle(40, 40, 200, 200)); // extrapola o bitmap

        var excecao = Record.Exception(() => Renderizar(forma, origem).Dispose());

        Assert.Null(excecao);
    }

    [Fact]
    public void Borrao_SemImagemDeOrigem_CobreAAreaSemRevelarOConteudo()
    {
        // Sem os pixels não há o que pixelizar; o contrato é nunca deixar o
        // conteúdo aparecer, então a área é preenchida.
        using var origem = CriarOrigem();
        var area = new Rectangle(20, 20, 40, 40);

        using var destino = new Bitmap(origem.Width, origem.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(destino))
        {
            g.DrawImage(origem, Point.Empty);
            ShapeRenderer.Draw(g, Borrao(area), origem: null);
        }

        int iguaisAoOriginal = 0, total = 0;
        for (int y = area.Top + 2; y < area.Bottom - 2; y++)
            for (int x = area.Left + 2; x < area.Right - 2; x++)
            {
                total++;
                if (destino.GetPixel(x, y).ToArgb() == origem.GetPixel(x, y).ToArgb())
                    iguaisAoOriginal++;
            }

        Assert.True(iguaisAoOriginal < total * 0.1,
            "Sem imagem de origem, a área precisa ser coberta — não pode exibir o conteúdo.");
    }

    // ---------- Validade ----------

    [Fact]
    public void IsValid_ComBorraoDeArrasteSuficiente_Aceita() =>
        Assert.True(ShapeRenderer.IsValid(Borrao(new Rectangle(10, 10, 40, 40))));

    [Fact]
    public void IsValid_ComBorraoSemArraste_Descarta() =>
        Assert.False(ShapeRenderer.IsValid(new Shape { Tool = Tool.Blur, A = new Point(5, 5), B = new Point(6, 6) }));

    [Fact]
    public void IsValid_ComMarcadorDePasso_AceitaMesmoSemArraste()
    {
        // O marcador é posicionado por clique — exigir arraste o descartaria.
        var forma = new Shape { Tool = Tool.Step, A = new Point(50, 50), B = new Point(50, 50), StepNumber = 1 };

        Assert.True(ShapeRenderer.IsValid(forma));
    }

    // ---------- Marcador de passo ----------

    [Fact]
    public void Passo_DesenhaAlgoNoPontoInformado()
    {
        using var origem = CriarOrigem();
        var forma = new Shape
        {
            Tool = Tool.Step,
            Color = Color.Red,
            Thickness = 4,
            A = new Point(60, 60),
            StepNumber = 3,
        };

        using var resultado = Renderizar(forma, origem);

        // O centro cai sobre o algarismo, que é branco sobre vermelho — e
        // branco também existe no xadrez original, então não distingue nada.
        // O ponto verificado fica no preenchimento do círculo, entre o
        // algarismo e a borda, onde só pode haver a cor do marcador.
        var noPreenchimento = resultado.GetPixel(60 + 14, 60);
        Assert.True(noPreenchimento.R > 200 && noPreenchimento.G < 80 && noPreenchimento.B < 80,
            $"O preenchimento do marcador deveria ser vermelho; veio {noPreenchimento}.");
    }

    [Fact]
    public void Passo_ComNumeroDeDoisDigitos_NaoLanca()
    {
        using var origem = CriarOrigem();
        var forma = new Shape { Tool = Tool.Step, Color = Color.Blue, Thickness = 2, A = new Point(60, 60), StepNumber = 42 };

        var excecao = Record.Exception(() => Renderizar(forma, origem).Dispose());

        Assert.Null(excecao);
    }

    // ---------- Ferramentas existentes seguem funcionando ----------

    [Theory]
    [InlineData("Rect")]
    [InlineData("Ellipse")]
    [InlineData("Line")]
    [InlineData("Arrow")]
    public void FormasVetoriais_DesenhamSemLancar(string ferramenta)
    {
        using var origem = CriarOrigem();
        var forma = new Shape
        {
            Tool = Enum.Parse<Tool>(ferramenta),
            Color = Color.Lime,
            Thickness = 3,
            A = new Point(20, 20),
            B = new Point(90, 80),
        };

        var excecao = Record.Exception(() => Renderizar(forma, origem).Dispose());

        Assert.Null(excecao);
    }

    [Fact]
    public void Lapis_ComVariosPontos_DesenhaSemLancar()
    {
        using var origem = CriarOrigem();
        var forma = new Shape
        {
            Tool = Tool.Pen,
            Color = Color.Cyan,
            Thickness = 2,
            Points = new List<Point> { new(10, 10), new(30, 40), new(60, 20) },
        };

        var excecao = Record.Exception(() => Renderizar(forma, origem).Dispose());

        Assert.Null(excecao);
    }
}
