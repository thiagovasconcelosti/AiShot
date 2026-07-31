using System.Drawing;
using System.Drawing.Imaging;
using AiShot.Capture;
using AiShot.UI;

namespace AiShot.Tests;

/// <summary>
/// Amostra visual do contorno de foco do teclado, para conferência a olho.
/// </summary>
/// <remarks>
/// Sempre passa: existe para que quem mexer no desenho veja o resultado sem
/// abrir o aplicativo. Desenha as duas barras com um botão focado em cada.
/// </remarks>
public class FocusRingVisualDump
{
    [Fact]
    public void GerarAmostraDoFoco()
    {
        var monitor = new Rectangle(0, 0, 900, 500);
        var selecao = new Rectangle(180, 120, 420, 240);
        var layout = ToolbarLayout.Compute(selecao, monitor, Tool.Pen, paletteOpen: false, thicknessOpen: false);

        using var saida = new Bitmap(monitor.Width, monitor.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(saida))
        {
            // Fundo escurecido, como o overlay desenha por cima do print.
            g.Clear(Color.FromArgb(255, 30, 30, 34));
            using (var claro = new SolidBrush(Color.FromArgb(255, 70, 70, 78)))
                g.FillRectangle(claro, selecao);

            // Um botão focado na barra lateral e outro na inferior.
            ToolbarRenderer.DrawToolbars(g, layout, Color.Red, focado: "arrow");

            using var rotulo = new Font("Segoe UI", 11);
            using var tinta = new SolidBrush(Theme.TextMuted);
            g.DrawString("foco do teclado: botão 'arrow' na barra lateral", rotulo, tinta, 10, 10);
        }

        var caminho = AmostraVisual.Gravar(saida, "amostra-foco.png");

        Assert.True(File.Exists(caminho));
    }
}
