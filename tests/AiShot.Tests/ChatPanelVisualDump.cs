using System.Drawing;
using System.Drawing.Imaging;
using AiShot.Capture;

namespace AiShot.Tests;

/// <summary>
/// Amostra visual do desenho por blocos do chat, para conferência a olho.
/// </summary>
/// <remarks>
/// Sempre passa: existe para que quem mexer no desenho veja o resultado sem
/// abrir o aplicativo. Exercita o mesmo par medir/desenhar que o balão usa,
/// alcançado por reflexão porque são detalhes internos do ChatPanel.
/// </remarks>
public class ChatPanelVisualDump
{
    [Fact]
    public void GerarAmostraDoChat()
    {
        const string resposta = """
            Para ler o arquivo, use o trecho abaixo:

            ```csharp
            var texto = File.ReadAllText(caminho);
            if (texto.Length > 0)
            {
                Console.WriteLine(texto);
            }
            ```

            Pontos de atenção:

            - O caminho precisa ser absoluto
            - Trate `FileNotFoundException`
            1. Verifique a permissão de leitura

            Depois disso, a variável **texto** já contém o conteúdo.
            """;

        var blocos = MarkdownBlocks.Parse(resposta);

        const int largura = 420, padding = 14;
        using var medidor = Graphics.FromImage(new Bitmap(1, 1));
        int altura = InvocarMedir(medidor, blocos, largura - padding * 2);

        using var saida = new Bitmap(largura, altura + padding * 2, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(saida))
        {
            g.Clear(Color.FromArgb(24, 24, 27)); // fundo do balão do assistente
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            InvocarDesenhar(g, blocos,
                new Rectangle(padding, padding, largura - padding * 2, altura),
                Color.FromArgb(228, 228, 231));
        }

        var caminho = Path.Combine(AppContext.BaseDirectory, "amostra-chat.png");
        saida.Save(caminho, ImageFormat.Png);

        Assert.True(File.Exists(caminho));
    }

    private static int InvocarMedir(Graphics g, List<Block> blocos, int largura) =>
        (int)Metodo("MedirBlocos").Invoke(null, [g, blocos, largura])!;

    private static void InvocarDesenhar(Graphics g, List<Block> blocos, Rectangle area, Color cor) =>
        Metodo("DesenharBlocos").Invoke(null, [g, blocos, area, cor]);

    private static System.Reflection.MethodInfo Metodo(string nome) =>
        typeof(ChatPanel).GetMethod(nome,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
        ?? throw new InvalidOperationException($"{nome} não encontrado — o teste precisa acompanhar o ChatPanel.");
}
