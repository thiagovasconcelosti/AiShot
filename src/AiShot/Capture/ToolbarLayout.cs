using System.Drawing;
using AiShot.UI;

namespace AiShot.Capture;

/// <summary>Resultado do cálculo de layout das toolbars (posições, sem desenho).</summary>
internal readonly record struct ToolbarLayoutResult(
    Rectangle SidePanel,
    List<IconButton> SideButtons,
    Rectangle BottomPanel,
    List<IconButton> BottomButtons);

/// <summary>
/// Calcula a posição das toolbars (lateral de ferramentas e inferior de ações)
/// em relação à seleção e ao monitor, com anti-colisão. Puro — sem UI/estado.
/// </summary>
internal static class ToolbarLayout
{
    private static readonly (string glyph, string id, string tip, Tool tool)[] Tools =
    {
        (Icons.Pencil, "pen", "Lápis", Tool.Pen),
        (Icons.Arrow, "arrow", "Seta", Tool.Arrow),
        (Icons.Line, "line", "Linha", Tool.Line),
        (Icons.Rectangle, "rect", "Retângulo", Tool.Rect),
        (Icons.Circle, "ellipse", "Elipse", Tool.Ellipse),
        (Icons.Text, "text", "Texto", Tool.Text),
        (Icons.Palette, "color", "Cor", Tool.None),
        ("", "thickness", "Espessura", Tool.None),
        (Icons.Undo, "undo", "Desfazer", Tool.None),
    };

    private static readonly (string glyph, string id, string tip)[] Actions =
    {
        (Icons.Copy, "copy", "Copiar"),
        (Icons.Save, "save", "Salvar"),
        (Icons.Paint, "paint", "Abrir no Paint"),
        (Icons.Upload, "upload", "Upload"),
        (Icons.Share, "share", "Compartilhar"),
        (Icons.Chat, "ai", "Perguntar à IA"),
        (Icons.Close, "close", "Fechar"),
    };

    public static ToolbarLayoutResult Compute(Rectangle sel, Rectangle mon, Tool tool, bool paletteOpen, bool thicknessOpen)
    {
        int bs = Theme.ButtonSize, gap = 2, pad = Theme.BarPad;

        // --- Barra lateral ---
        int panelW = bs + pad * 2;
        int panelH = Tools.Length * bs + (Tools.Length - 1) * gap + pad * 2;
        int sx = sel.Right + 12;
        if (sx + panelW > mon.Right - 8) sx = sel.Left - 12 - panelW;
        if (sx < mon.Left + 8) sx = mon.Right - 8 - panelW; // último recurso
        int sy = Math.Max(mon.Top + 8, Math.Min(sel.Top, mon.Bottom - panelH - 8));
        var sidePanel = new Rectangle(sx, sy, panelW, panelH);

        var sideButtons = new List<IconButton>(Tools.Length);
        for (int i = 0; i < Tools.Length; i++)
        {
            var r = new Rectangle(sx + pad, sy + pad + i * (bs + gap), bs, bs);
            bool active = Tools[i].id switch
            {
                "color" => paletteOpen,
                "thickness" => thicknessOpen,
                _ => tool == Tools[i].tool && Tools[i].tool != Tool.None,
            };
            sideButtons.Add(new IconButton(r, Tools[i].glyph, Tools[i].id, active, Tools[i].tip));
        }

        // --- Barra inferior ---
        int bw = Actions.Length * bs + (Actions.Length - 1) * gap + pad * 2;
        int bx = sel.Left + (sel.Width - bw) / 2;
        bx = Math.Max(mon.Left + 8, Math.Min(bx, mon.Right - bw - 8));
        int bh = bs + pad * 2;
        int by = sel.Bottom + 12;
        if (by + bh > mon.Bottom - 8) by = sel.Top - 12 - bh;            // tenta acima
        by = Math.Max(mon.Top + 8, Math.Min(by, mon.Bottom - bh - 8));   // clampa no monitor (fullscreen: sobrepõe)
        var botPanel = new Rectangle(bx, by, bw, bh);

        // Evita sobrepor a toolbar lateral: empurra pro lado oposto a ela.
        if (botPanel.IntersectsWith(sidePanel))
        {
            bool sideRight = sidePanel.Left >= sel.Right;
            bx = sideRight ? sidePanel.Left - 8 - bw : sidePanel.Right + 8;
            bx = Math.Max(mon.Left + 8, Math.Min(bx, mon.Right - bw - 8));
            botPanel = new Rectangle(bx, by, bw, bs + pad * 2);
        }

        var bottomButtons = new List<IconButton>(Actions.Length);
        for (int i = 0; i < Actions.Length; i++)
        {
            var r = new Rectangle(bx + pad + i * (bs + gap), by + pad, bs, bs);
            bottomButtons.Add(new IconButton(r, Actions[i].glyph, Actions[i].id, false, Actions[i].tip));
        }

        return new ToolbarLayoutResult(sidePanel, sideButtons, botPanel, bottomButtons);
    }
}
