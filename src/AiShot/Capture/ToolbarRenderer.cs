using System.Drawing;
using System.Drawing.Drawing2D;
using AiShot.UI;

namespace AiShot.Capture;

/// <summary>
/// Desenho das barras de ferramentas e dos menus suspensos de cor e espessura.
/// </summary>
/// <remarks>
/// Sem estado: recebe o layout já calculado e devolve as áreas clicáveis dos
/// menus, para que o overlay faça o teste de acerto sem repetir a matemática de
/// posicionamento.
/// </remarks>
internal static class ToolbarRenderer
{
    private static readonly StringFormat CenterFmt =
        new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    /// <summary>Desenha as duas barras e os botões.</summary>
    public static void DrawToolbars(Graphics g, ToolbarLayoutResult layout, Color corAtual)
    {
        Theme.DrawPanel(g, layout.SidePanel);
        foreach (var b in layout.SideButtons)
        {
            DrawButton(g, b);

            // Dois botões ganham um indicador do valor em uso, além do ícone.
            if (b.Id == "color")
            {
                var ponto = new Rectangle(b.Rect.Right - 12, b.Rect.Bottom - 12, 7, 7);
                using var pincel = new SolidBrush(corAtual);
                g.FillEllipse(pincel, ponto);
            }
            else if (b.Id == "thickness")
            {
                DrawThicknessGlyph(g, b.Rect, b.Active ? Color.Black : Theme.Text);
            }
            else if (b.Id == "blur")
            {
                DrawBlurGlyph(g, b.Rect, b.Active ? Color.Black : Theme.Text);
            }
            else if (b.Id == "step")
            {
                DrawStepGlyph(g, b.Rect, b.Active ? Color.Black : Theme.Text);
            }
        }

        Theme.DrawPanel(g, layout.BottomPanel);
        foreach (var b in layout.BottomButtons)
            DrawButton(g, b, discreto: b.Id == "close");
    }

    private static void DrawButton(Graphics g, IconButton b, bool discreto = false)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (b.Active)
        {
            using var realce = new SolidBrush(Color.White);
            using var p = Theme.RoundRect(b.Rect, 8);
            g.FillPath(realce, p);
        }

        var cor = b.Active ? Color.Black : (discreto ? Theme.TextMuted : Theme.Text);
        using var pincel = new SolidBrush(cor);
        g.DrawString(b.Glyph, Icons.Cached(20), pincel, b.Rect, CenterFmt);
    }

    /// <summary>Ícone do botão de espessura: três barras de peso crescente.</summary>
    private static void DrawThicknessGlyph(Graphics g, Rectangle destino, Color cor)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int[] larguras = { 10, 14, 18 };
        int[] alturas = { 2, 3, 4 };
        const int gap = 3;

        int alturaTotal = alturas.Sum() + gap * (alturas.Length - 1);
        int centroX = destino.X + destino.Width / 2;
        int y = destino.Y + (destino.Height - alturaTotal) / 2;

        using var pincel = new SolidBrush(cor);
        for (int i = 0; i < larguras.Length; i++)
        {
            g.FillRectangle(pincel, centroX - larguras[i] / 2, y, larguras[i], alturas[i]);
            y += alturas[i] + gap;
        }
    }

    /// <summary>
    /// Ícone do borrão: uma grade de blocos, alguns preenchidos — a leitura
    /// visual do que a ferramenta faz com os pixels.
    /// </summary>
    private static void DrawBlurGlyph(Graphics g, Rectangle destino, Color cor)
    {
        var suavizacaoAnterior = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        try
        {
            const int colunas = 4, linhas = 4, lado = 4, gap = 1;
            int larguraTotal = colunas * lado + (colunas - 1) * gap;
            int alturaTotal = linhas * lado + (linhas - 1) * gap;
            int x0 = destino.X + (destino.Width - larguraTotal) / 2;
            int y0 = destino.Y + (destino.Height - alturaTotal) / 2;

            // Padrão xadrez irregular: sugere blocos de cor sem virar um tabuleiro.
            bool[,] preenchido =
            {
                { true, false, true, true },
                { false, true, true, false },
                { true, true, false, true },
                { true, false, true, false },
            };

            using var forte = new SolidBrush(cor);
            using var fraco = new SolidBrush(Color.FromArgb(90, cor));
            for (int l = 0; l < linhas; l++)
                for (int c = 0; c < colunas; c++)
                    g.FillRectangle(
                        preenchido[l, c] ? forte : fraco,
                        x0 + c * (lado + gap), y0 + l * (lado + gap), lado, lado);
        }
        finally { g.SmoothingMode = suavizacaoAnterior; }
    }

    /// <summary>Ícone da numeração: um círculo com o algarismo 1.</summary>
    private static void DrawStepGlyph(Graphics g, Rectangle destino, Color cor)
    {
        var suavizacaoAnterior = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            const int raio = 9;
            var circulo = new Rectangle(
                destino.X + destino.Width / 2 - raio,
                destino.Y + destino.Height / 2 - raio,
                raio * 2, raio * 2);

            using (var contorno = new Pen(cor, 1.6f))
                g.DrawEllipse(contorno, circulo);

            using var fonte = new Font("Segoe UI", raio, FontStyle.Bold, GraphicsUnit.Pixel);
            using var pincel = new SolidBrush(cor);
            using var centralizado = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString("1", fonte, pincel, circulo, centralizado);
        }
        finally { g.SmoothingMode = suavizacaoAnterior; }
    }

    /// <summary>
    /// Desenha a paleta ancorada ao botão de cor.
    /// </summary>
    /// <returns>As áreas clicáveis de cada cor, para o teste de acerto.</returns>
    public static List<(Rectangle Area, Color Cor)> DrawPalette(
        Graphics g, IReadOnlyList<IconButton> botoesLaterais, Color corAtual)
    {
        var areas = new List<(Rectangle, Color)>();

        var botao = botoesLaterais.FirstOrDefault(b => b.Id == "color");
        if (botao is null) return areas;

        const int lado = 22, gap = 6, pad = 8, colunas = 4;
        var paleta = AnnotationController.Palette;
        int linhas = (int)Math.Ceiling(paleta.Length / (double)colunas);
        int w = colunas * lado + (colunas - 1) * gap + pad * 2;
        int h = linhas * lado + (linhas - 1) * gap + pad * 2;

        // À esquerda do botão; à direita quando não couber.
        int x = botao.Rect.Left - w - 8;
        if (x < 8) x = botao.Rect.Right + 8;
        int y = botao.Rect.Top;

        Theme.DrawPanel(g, new Rectangle(x, y, w, h));

        for (int i = 0; i < paleta.Length; i++)
        {
            int coluna = i % colunas, linha = i / colunas;
            var area = new Rectangle(x + pad + coluna * (lado + gap), y + pad + linha * (lado + gap), lado, lado);
            areas.Add((area, paleta[i]));

            using var pincel = new SolidBrush(paleta[i]);
            using var forma = Theme.RoundRect(area, 6);
            g.FillPath(pincel, forma);

            if (paleta[i].ToArgb() == corAtual.ToArgb())
            {
                using var contorno = new Pen(Color.White, 2);
                g.DrawPath(contorno, forma);
            }
        }

        return areas;
    }

    /// <summary>
    /// Desenha o menu de espessura ancorado ao botão correspondente. Cada opção
    /// mostra uma linha na espessura que representa.
    /// </summary>
    /// <returns>As áreas clicáveis de cada nível, para o teste de acerto.</returns>
    public static List<(Rectangle Area, int Espessura)> DrawThicknessMenu(
        Graphics g, IReadOnlyList<IconButton> botoesLaterais, int espessuraAtual)
    {
        var areas = new List<(Rectangle, int)>();

        var botao = botoesLaterais.FirstOrDefault(b => b.Id == "thickness");
        if (botao is null) return areas;

        const int bw = 46, bh = 34, gap = 6, pad = 8;
        var niveis = AnnotationController.ThicknessLevels;
        int w = niveis.Length * bw + (niveis.Length - 1) * gap + pad * 2;
        int h = bh + pad * 2;

        int x = botao.Rect.Left - w - 8;
        if (x < 8) x = botao.Rect.Right + 8;
        int y = botao.Rect.Top;

        Theme.DrawPanel(g, new Rectangle(x, y, w, h));

        for (int i = 0; i < niveis.Length; i++)
        {
            var area = new Rectangle(x + pad + i * (bw + gap), y + pad, bw, bh);
            areas.Add((area, niveis[i]));

            bool selecionado = niveis[i] == espessuraAtual;
            using (var forma = Theme.RoundRect(area, 8))
            using (var fundo = new SolidBrush(selecionado ? Color.White : Theme.SurfaceHover))
                g.FillPath(fundo, forma);

            using var caneta = new Pen(selecionado ? Color.Black : Theme.Text, niveis[i])
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            int meioY = area.Y + area.Height / 2;
            g.DrawLine(caneta, area.X + 10, meioY, area.Right - 10, meioY);
        }

        return areas;
    }
}
