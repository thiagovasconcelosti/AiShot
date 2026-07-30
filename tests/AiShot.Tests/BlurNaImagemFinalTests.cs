using System.Drawing;
using System.Drawing.Imaging;
using AiShot.Capture;

namespace AiShot.Tests;

/// <summary>
/// O borrão precisa valer na imagem exportada, não apenas no que aparece na
/// tela.
/// </summary>
/// <remarks>
/// A imagem final é produzida por um caminho próprio: recorta a seleção do
/// fundo e reaplica os shapes sobre um bitmap novo, com uma translação de
/// coordenadas no meio. Um erro nessa conversão desenharia o borrão deslocado —
/// a tela mostraria a área coberta e o arquivo salvo entregaria o conteúdo.
/// Este teste reproduz esse caminho.
/// </remarks>
public class BlurNaImagemFinalTests
{
    /// <summary>Reproduz o que CaptureOverlay.RenderFinal faz.</summary>
    private static Bitmap RenderizarComoFinal(Bitmap fundo, Rectangle selecao, params Shape[] formas)
    {
        var saida = new Bitmap(selecao.Width, selecao.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(saida);

        g.DrawImage(fundo, new Rectangle(0, 0, selecao.Width, selecao.Height), selecao, GraphicsUnit.Pixel);
        g.TranslateTransform(-selecao.Left, -selecao.Top);
        foreach (var f in formas) ShapeRenderer.Draw(g, f, fundo);

        return saida;
    }

    /// <summary>
    /// Fundo em xadrez de um pixel, com a cor variando por faixa vertical.
    /// </summary>
    /// <remarks>
    /// O xadrez torna qualquer resíduo do original detectável; as faixas de cor
    /// tornam detectável um erro de coordenadas. Com um fundo uniforme, ler a
    /// região errada produziria blocos igualmente uniformes e o defeito
    /// passaria — foi o que aconteceu na primeira versão deste arquivo.
    /// </remarks>
    private static Bitmap CriarFundo(int largura, int altura)
    {
        var bmp = new Bitmap(largura, altura, PixelFormat.Format32bppArgb);
        for (int y = 0; y < altura; y++)
        {
            // Faixas de 30 pixels, cada uma com um tom bem distinto.
            int faixa = (y / 30) % 3;
            var claro = faixa switch
            {
                0 => Color.FromArgb(255, 240, 40, 40),
                1 => Color.FromArgb(255, 40, 240, 40),
                _ => Color.FromArgb(255, 40, 40, 240),
            };
            for (int x = 0; x < largura; x++)
                bmp.SetPixel(x, y, (x + y) % 2 == 0 ? Color.Black : claro);
        }
        return bmp;
    }

    [Fact]
    public void Borrao_ValeNaImagemExportada_ENaoApenasNaTela()
    {
        using var fundo = CriarFundo(300, 300);
        // Seleção deslocada da origem: é o caso em que um erro de translação
        // apareceria. Com a seleção no canto, o defeito passaria despercebido.
        var selecao = new Rectangle(80, 60, 140, 120);

        var borrao = new Shape
        {
            Tool = Tool.Blur,
            A = new Point(selecao.Left + 20, selecao.Top + 20),
            B = new Point(selecao.Right - 20, selecao.Bottom - 20),
        };

        using var final = RenderizarComoFinal(fundo, selecao, borrao);

        // Área do borrão em coordenadas da imagem final.
        var naFinal = new Rectangle(20, 20, selecao.Width - 40, selecao.Height - 40);

        int vizinhosIguais = 0, total = 0;
        for (int y = naFinal.Top + 2; y < naFinal.Bottom - 2; y++)
            for (int x = naFinal.Left + 2; x < naFinal.Right - 3; x++)
            {
                total++;
                if (final.GetPixel(x, y).ToArgb() == final.GetPixel(x + 1, y).ToArgb())
                    vizinhosIguais++;
            }

        Assert.True(vizinhosIguais > total * 0.5,
            $"A área borrada não virou blocos na imagem exportada: {vizinhosIguais}/{total} vizinhos iguais. " +
            "Provável erro de conversão de coordenadas entre a tela e o arquivo.");
    }

    [Fact]
    public void Borrao_NaImagemExportada_NaoDeslocaAArea()
    {
        using var fundo = CriarFundo(300, 300);
        var selecao = new Rectangle(80, 60, 140, 120);

        var borrao = new Shape
        {
            Tool = Tool.Blur,
            A = new Point(selecao.Left + 30, selecao.Top + 30),
            B = new Point(selecao.Left + 90, selecao.Top + 80),
        };

        using var final = RenderizarComoFinal(fundo, selecao, borrao);

        // Fora da área borrada o xadrez precisa continuar intacto: se o borrão
        // tivesse escorregado, este ponto teria sido coberto.
        Assert.NotEqual(final.GetPixel(5, 5).ToArgb(), final.GetPixel(6, 5).ToArgb());
    }

    [Fact]
    public void Borrao_LeOsPixeisDaRegiaoCorreta()
    {
        // O fundo tem faixas de cor: se a leitura dos pixels usar coordenadas
        // erradas, os blocos saem com a cor de outra faixa. Um asserto de
        // "virou bloco uniforme" não pegaria isso — blocos da região errada
        // também são uniformes.
        using var fundo = CriarFundo(300, 300);
        var selecao = new Rectangle(80, 60, 140, 120);

        // Faixas do fundo em y da TELA, por (y / 30) % 3: 0..29 vermelha,
        // 30..59 verde, 60..89 azul, e assim por diante. A área do borrão fica
        // dentro da faixa azul, com folga das bordas.
        const int topoNaTela = 64, baseNaTela = 86;
        var borrao = new Shape
        {
            Tool = Tool.Blur,
            A = new Point(selecao.Left + 10, topoNaTela),
            B = new Point(selecao.Left + 90, baseNaTela),
        };

        using var final = RenderizarComoFinal(fundo, selecao, borrao);

        // Converte para coordenadas da imagem final e amostra o miolo, evitando
        // os blocos de borda, que misturam com o que está fora da área.
        int topo = topoNaTela - selecao.Top + 4;
        int baseY = baseNaTela - selecao.Top - 4;

        long somaR = 0, somaG = 0, somaB = 0;
        int amostras = 0;
        for (int y = topo; y < baseY; y++)
            for (int x = 20; x < 80; x++)
            {
                var p = final.GetPixel(x, y);
                somaR += p.R; somaG += p.G; somaB += p.B;
                amostras++;
            }

        double mediaR = (double)somaR / amostras;
        double mediaG = (double)somaG / amostras;
        double mediaB = (double)somaB / amostras;

        // O xadrez alterna preto com azul nessa faixa, então a média puxa para
        // o azul. Ler a faixa vermelha ou a verde inverteria a relação.
        Assert.True(mediaB > mediaR && mediaB > mediaG,
            $"Os blocos saíram com a cor de outra faixa (R={mediaR:F0} G={mediaG:F0} B={mediaB:F0}) — " +
            "a leitura dos pixels está usando coordenadas erradas.");
    }

    [Fact]
    public void MarcadorDePasso_ApareceNaImagemExportada()
    {
        using var fundo = CriarFundo(300, 300);
        var selecao = new Rectangle(50, 50, 200, 200);

        var passo = new Shape
        {
            Tool = Tool.Step,
            Color = Color.FromArgb(239, 68, 68),
            Thickness = 4,
            A = new Point(selecao.Left + 100, selecao.Top + 100),
            StepNumber = 1,
        };

        using var final = RenderizarComoFinal(fundo, selecao, passo);

        // No preenchimento do marcador, em coordenadas da imagem final.
        var noPreenchimento = final.GetPixel(100 + 14, 100);
        Assert.True(noPreenchimento.R > 200 && noPreenchimento.G < 80 && noPreenchimento.B < 80,
            $"O marcador não apareceu na posição esperada da imagem exportada; veio {noPreenchimento}.");
    }
}
