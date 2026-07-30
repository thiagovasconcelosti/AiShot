using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AiShot.UI;

namespace AiShot.Capture;

/// <summary>
/// Elementos informativos desenhados sobre o print: dica de ferramenta,
/// mensagem transitória e o tamanho da seleção.
/// </summary>
/// <remarks>
/// Guarda o próprio estado (qual dica está visível, qual mensagem foi emitida)
/// e sabe apenas desenhar — não trata entrada nem conhece as ações do overlay.
/// </remarks>
internal sealed class OverlayChrome
{
    // Recursos GDI reaproveitados entre quadros, para não alocar a cada pintura.
    private static readonly StringFormat CenterFmt =
        new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    private static readonly Font TooltipFont = new("Segoe UI", 8.5f);
    private static readonly Font DimFont = new("Segoe UI", 9f, FontStyle.Bold);
    private static readonly Font FlashFont = new("Segoe UI", 9.5f);

    private string? _dica;
    private Rectangle _dicaAlvo;
    private string? _mensagem;

    /// <summary>Mensagem transitória exibida no topo do monitor principal.</summary>
    public void Flash(string mensagem) => _mensagem = mensagem;

    /// <summary>
    /// Atualiza a dica conforme o botão sob o cursor.
    /// </summary>
    /// <returns>true se a dica mudou e a tela precisa ser repintada.</returns>
    public bool UpdateHover(Point cursor, IEnumerable<IconButton> primeiro, IEnumerable<IconButton> depois)
    {
        var alvo = Encontrar(primeiro, cursor) ?? Encontrar(depois, cursor);

        var dica = alvo?.Tip;
        if (dica == _dica) return false;

        _dica = dica;
        _dicaAlvo = alvo?.Rect ?? Rectangle.Empty;
        return true;
    }

    private static IconButton? Encontrar(IEnumerable<IconButton> botoes, Point cursor)
    {
        foreach (var b in botoes)
            if (b.Rect.Contains(cursor)) return b;
        return null;
    }

    /// <summary>Desenha a dica de ferramenta, se houver botão sob o cursor.</summary>
    public void DrawTooltip(Graphics g, int larguraDaTela)
    {
        if (_dica is null || _dicaAlvo.IsEmpty) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var tamanho = g.MeasureString(_dica, TooltipFont);
        int w = (int)tamanho.Width + 16, h = 24;

        // Acima do botão por padrão; abaixo quando não couber.
        int x = _dicaAlvo.Left + _dicaAlvo.Width / 2 - w / 2;
        int y = _dicaAlvo.Top - h - 8;
        if (y < 4) y = _dicaAlvo.Bottom + 8;
        x = Math.Max(4, Math.Min(x, larguraDaTela - w - 4));

        var r = new Rectangle(x, y, w, h);
        using (var p = Theme.RoundRect(r, 7))
        using (var fundo = new SolidBrush(Color.FromArgb(252, 24, 24, 27)))
        using (var borda = new Pen(Theme.Border, 1))
        {
            g.FillPath(fundo, p);
            g.DrawPath(borda, p);
        }
        using var texto = new SolidBrush(Theme.Text);
        g.DrawString(_dica, TooltipFont, texto, r, CenterFmt);
    }

    /// <summary>Desenha o tamanho da seleção, logo acima dela.</summary>
    public void DrawDimensions(Graphics g, Rectangle selecao)
    {
        if (selecao.Width <= 0) return;

        var txt = $"{selecao.Width} × {selecao.Height}";
        var tamanho = g.MeasureString(txt, DimFont);

        int y = selecao.Top - 24;
        if (y < 4) y = selecao.Top + 6; // sem espaço acima: desce para dentro
        var fundo = new Rectangle(selecao.Left, y, (int)tamanho.Width + 14, 20);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = Theme.RoundRect(fundo, 6))
        using (var b = new SolidBrush(Theme.Surface))
            g.FillPath(b, p);
        using (var texto = new SolidBrush(Theme.Text))
            g.DrawString(txt, DimFont, texto, fundo.Left + 7, fundo.Top + 3);
    }

    /// <summary>
    /// Desenha a mensagem transitória centralizada no monitor principal — e não
    /// na área virtual, que numa configuração de vários monitores colocaria o
    /// aviso na emenda entre telas.
    /// </summary>
    public void DrawFlash(Graphics g)
    {
        if (_mensagem is null) return;

        var tamanho = g.MeasureString(_mensagem, FlashFont);
        int w = (int)tamanho.Width + 24;

        var virtual_ = SystemInformation.VirtualScreen;
        var principal = Screen.PrimaryScreen!.Bounds;
        var emCoordenadasDoForm = new Rectangle(
            principal.X - virtual_.X, principal.Y - virtual_.Y, principal.Width, principal.Height);

        var r = new Rectangle(
            emCoordenadasDoForm.Left + (emCoordenadasDoForm.Width - w) / 2,
            emCoordenadasDoForm.Top + 24, w, 30);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = Theme.RoundRect(r, 8))
        using (var fundo = new SolidBrush(Theme.Surface))
        using (var borda = new Pen(Theme.Border, 1))
        {
            g.FillPath(fundo, p);
            g.DrawPath(borda, p);
        }
        using var texto = new SolidBrush(Theme.Text);
        g.DrawString(_mensagem, FlashFont, texto, r, CenterFmt);
    }
}
