using System.Drawing;
using System.Drawing.Imaging;
using AiShot.Capture;

namespace AiShot.Tests;

/// <summary>
/// Gera uma amostra visual das ferramentas de anotação para conferência a olho.
/// </summary>
/// <remarks>
/// Não é um teste de regressão: sempre passa. Existe para que quem mexer no
/// desenho consiga olhar o resultado sem abrir o aplicativo — comparar imagens
/// pixel a pixel entre versões do Windows seria frágil demais para valer como
/// asserto. O arquivo vai para a pasta de saída dos testes.
/// </remarks>
public class ShapeRendererVisualDump
{
    [Fact]
    public void GerarAmostraVisual()
    {
        const int largura = 640, altura = 260;

        // Fundo com texto legível: é o que o borrão precisa tornar ilegível.
        using var fundo = new Bitmap(largura, altura, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(fundo))
        {
            g.Clear(Color.FromArgb(250, 250, 252));
            using var fonte = new Font("Segoe UI", 14f, FontStyle.Bold);
            using var tinta = new SolidBrush(Color.FromArgb(20, 20, 30));
            for (int i = 0; i < 6; i++)
                g.DrawString($"SENHA-SECRETA-{i:000}  cartão 4111 1111 1111 111{i}",
                    fonte, tinta, 16, 16 + i * 28);
        }

        using var saida = new Bitmap(largura, altura, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(saida))
        {
            g.DrawImage(fundo, Point.Empty);

            // Borrão cobrindo as três primeiras linhas por inteiro.
            ShapeRenderer.Draw(g, new Shape
            {
                Tool = Tool.Blur,
                A = new Point(12, 12),
                B = new Point(462, 96),
            }, fundo);

            // Marcadores numerados, em cores diferentes.
            var cores = new[] { Color.FromArgb(239, 68, 68), Color.FromArgb(59, 130, 246), Color.FromArgb(34, 197, 94) };
            for (int i = 0; i < 3; i++)
                ShapeRenderer.Draw(g, new Shape
                {
                    Tool = Tool.Step,
                    Color = cores[i],
                    Thickness = 4,
                    A = new Point(480 + i * 50, 60),
                    StepNumber = i + 1,
                }, fundo);

            // Uma seta e um retângulo, para comparar com o que já existia.
            ShapeRenderer.Draw(g, new Shape
            {
                Tool = Tool.Arrow,
                Color = Color.FromArgb(249, 115, 22),
                Thickness = 4,
                A = new Point(470, 210),
                B = new Point(600, 150),
            }, fundo);
            ShapeRenderer.Draw(g, new Shape
            {
                Tool = Tool.Rect,
                Color = Color.FromArgb(168, 85, 247),
                Thickness = 3,
                A = new Point(20, 180),
                B = new Point(300, 240),
            }, fundo);
        }

        var caminho = Path.Combine(AppContext.BaseDirectory, "amostra-anotacoes.png");
        saida.Save(caminho, ImageFormat.Png);

        Assert.True(File.Exists(caminho));
    }
}
