using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using AiShot.UI;

namespace AiShot.Capture;

/// <summary>
/// Overlay único estilo Lightshot: seleção de região + edição in-place sem
/// fechar a tela. Toolbar lateral (ferramentas de desenho), barra inferior
/// (ações) e balão de chat para a IA — tudo sobre o print, com a área ainda
/// selecionada e redimensionável. Estética dark "shadcn/Geist".
/// </summary>
public sealed class CaptureOverlay : Form
{
    private enum Mode { Selecting, Editing }

    private readonly Bitmap _background;
    private readonly ICaptureServices _services;

    private Mode _mode = Mode.Selecting;
    private Tool _tool = Tool.None;
    private Rectangle _sel;
    private Point _dragStart;
    private bool _dragging;
    private ResizeHandle _activeHandle = ResizeHandle.None;
    private Rectangle _selAtDragStart;

    private readonly List<Shape> _shapes = new();
    private Shape? _drawing;
    private Color _color = Color.FromArgb(239, 68, 68); // vermelho (padrão, igual referência)
    private static readonly int[] ThicknessLevels = { 2, 4, 7 }; // fina, média, grossa
    private int _thickness = ThicknessLevels[1];

    // Layout recalculado a cada render
    private readonly List<IconButton> _sideButtons = new();
    private readonly List<IconButton> _bottomButtons = new();
    private readonly List<(Rectangle r, Color c)> _swatches = new();
    private readonly List<(Rectangle r, int v)> _thicknessSwatches = new();
    private bool _paletteOpen;
    private bool _thicknessMenuOpen;
    private Rectangle _sidePanelRect;

    // Chat (componente extraído) + cancelamento compartilhado (chat/upload)
    private readonly CancellationTokenSource _cts = new();
    private readonly ChatPanel _chat;
    private TextBox? _textInput; // ferramenta de texto

    private static readonly Color[] Palette =
    {
        Color.FromArgb(239,68,68), Color.FromArgb(249,115,22), Color.FromArgb(234,179,8),
        Color.FromArgb(34,197,94), Color.FromArgb(59,130,246), Color.FromArgb(168,85,247),
        Color.White, Color.FromArgb(24,24,27),
    };

    public CaptureOverlay(ICaptureServices services)
    {
        _services = services;
        _chat = new ChatPanel(this, () => _services.StartChat(RenderFinal()), _cts.Token);
        var vb = SystemInformation.VirtualScreen;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = vb;
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Color.Black;

        // Captura o fundo antes de aparecer.
        _background = new Bitmap(vb.Width, vb.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(_background))
            g.CopyFromScreen(vb.Left, vb.Top, 0, 0, vb.Size);

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    // ---------- Ciclo de vida ----------
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            if (_textInput is not null) { CancelTextInput(); Invalidate(); return; }
            if (_chat.IsOpen) { _chat.Close(); return; }
            Close();
            return;
        }
        if (e.Control && e.KeyCode == Keys.Z && _shapes.Count > 0)
        {
            _shapes.RemoveAt(_shapes.Count - 1);
            Invalidate();
        }
        base.OnKeyDown(e);
    }

    // ---------- Mouse ----------
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }

        if (_mode == Mode.Selecting)
        {
            _dragStart = e.Location;
            _sel = new Rectangle(e.Location, Size.Empty);
            _dragging = true;
            return;
        }

        // Edição: ordem de hit-test
        if (_chat.OnMouseDown(e.Location)) return;
        if (_paletteOpen)
        {
            foreach (var (r, c) in _swatches)
                if (r.Contains(e.Location)) { _color = c; _paletteOpen = false; Invalidate(); return; }
            _paletteOpen = false;
        }
        if (_thicknessMenuOpen)
        {
            foreach (var (r, v) in _thicknessSwatches)
                if (r.Contains(e.Location)) { _thickness = v; _thicknessMenuOpen = false; Invalidate(); return; }
            _thicknessMenuOpen = false;
        }
        foreach (var b in _bottomButtons)
            if (b.Rect.Contains(e.Location)) { OnBottomAction(b.Id); return; }
        foreach (var b in _sideButtons)
            if (b.Rect.Contains(e.Location)) { OnSideAction(b.Id, b.Rect); return; }

        var h = SelectionGeometry.HitHandle(_sel, e.Location);
        if (h != ResizeHandle.None)
        {
            _activeHandle = h;
            _selAtDragStart = _sel;
            _dragStart = e.Location;
            _dragging = true;
            return;
        }

        if (_sel.Contains(e.Location))
        {
            if (_tool == Tool.None) // mover seleção
            {
                _activeHandle = ResizeHandle.Move;
                _selAtDragStart = _sel;
                _dragStart = e.Location;
                _dragging = true;
            }
            else if (_tool == Tool.Text)
            {
                BeginTextInput(e.Location);
            }
            else // iniciar desenho
            {
                _drawing = new Shape { Tool = _tool, Color = _color, Thickness = _thickness, A = e.Location, B = e.Location };
                if (_tool == Tool.Pen) _drawing.Points = new List<Point> { e.Location };
                _dragging = true;
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_mode == Mode.Selecting && _dragging)
        {
            _sel = SelectionGeometry.Normalize(_dragStart, e.Location);
            Invalidate();
            return;
        }

        if (_mode == Mode.Editing)
        {
            UpdateCursor(e.Location);
            UpdateHoverTip(e.Location);

            if (_dragging && _activeHandle != ResizeHandle.None)
            {
                _sel = SelectionGeometry.ResizeOrMove(_activeHandle, _selAtDragStart, _dragStart, e.Location, _sel);
                Invalidate();
                return;
            }
            if (_dragging && _drawing is not null)
            {
                if (_drawing.Tool == Tool.Pen) _drawing.Points!.Add(e.Location);
                else _drawing.B = e.Location;
                Invalidate();
                return;
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_mode == Mode.Selecting && _dragging)
        {
            _dragging = false;
            if (_sel.Width > 8 && _sel.Height > 8)
            {
                _mode = Mode.Editing;
                Cursor = Cursors.Default;
            }
            else { Close(); }
            Invalidate();
            return;
        }

        if (_dragging)
        {
            _dragging = false;
            if (_drawing is not null)
            {
                if (ShapeRenderer.IsValid(_drawing)) _shapes.Add(_drawing);
                _drawing = null;
            }
            _activeHandle = ResizeHandle.None;
            _sel = SelectionGeometry.Clamp(_sel, new Size(Width, Height));
            Invalidate();
        }
        base.OnMouseUp(e);
    }

    // ---------- Ações ----------
    private void OnSideAction(string id, Rectangle btnRect)
    {
        switch (id)
        {
            case "pen": _tool = Toggle(Tool.Pen); break;
            case "arrow": _tool = Toggle(Tool.Arrow); break;
            case "line": _tool = Toggle(Tool.Line); break;
            case "rect": _tool = Toggle(Tool.Rect); break;
            case "ellipse": _tool = Toggle(Tool.Ellipse); break;
            case "text": _tool = Toggle(Tool.Text); break;
            case "color": _paletteOpen = !_paletteOpen; _thicknessMenuOpen = false; break;
            case "thickness": _thicknessMenuOpen = !_thicknessMenuOpen; _paletteOpen = false; break;
            case "undo": if (_shapes.Count > 0) _shapes.RemoveAt(_shapes.Count - 1); break;
        }
        Cursor = _tool == Tool.None ? Cursors.Default : Cursors.Cross;
        Invalidate();
    }

    private Tool Toggle(Tool t) => _tool == t ? Tool.None : t;

    private void OnBottomAction(string id)
    {
        switch (id)
        {
            case "copy": SafeSetClipboardImage(RenderFinal()); break;
            case "save": _services.SaveToFile(RenderFinal()); break;
            case "paint": OpenInPaint(); break;
            case "upload": _ = DoUploadAsync(); break;
            case "share": _ = DoUploadAsync(share: true); break;
            case "ai": _chat.Open(); break;
            case "close": Close(); break;
        }
    }

    private async Task DoUploadAsync(bool share = false)
    {
        try
        {
            Flash("Enviando…");
            var url = await _services.UploadAsync(RenderFinal(), _cts.Token).ConfigureAwait(true);
            if (IsDisposed) return;

            SafeSetClipboardText(url);
            if (share)
            {
                if (TryOpenUrl(url)) Flash("Aberto no navegador (URL copiada)");
                else Flash("URL inválida; não foi aberta. (copiada)");
            }
            else
            {
                Flash("URL copiada: " + url);
            }
        }
        catch (OperationCanceledException) { /* overlay fechado durante o upload */ }
        catch (Exception ex)
        {
            if (!IsDisposed) Flash((share ? "Falha ao compartilhar: " : "Falha no upload: ") + ex.Message);
        }
    }

    /// <summary>Abre uma URL só se for http/https (evita esquemas perigosos via ShellExecute).</summary>
    private static bool TryOpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = u.AbsoluteUri,
            UseShellExecute = true,
        });
        return true;
    }

    private void SafeSetClipboardText(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex) { Flash("Clipboard indisponível: " + ex.Message); }
    }

    private void SafeSetClipboardImage(Bitmap image)
    {
        try
        {
            _services.CopyToClipboard(image);
            if (_services.CloseOnCopy) { Close(); return; }
            Flash("Copiado");
        }
        catch (Exception ex) { Flash("Clipboard indisponível: " + ex.Message); }
    }

    /// <summary>Salva o print num arquivo temporário e abre no Paint (mspaint).</summary>
    private void OpenInPaint()
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"aishot_{Guid.NewGuid():N}.png");
            using (var bmp = RenderFinal())
                bmp.Save(path, ImageFormat.Png);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "mspaint.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
            Close(); // entrega pro Paint e fecha o overlay
        }
        catch (Exception ex) { Flash("Falha ao abrir o Paint: " + ex.Message); }
    }

    private string? _flash;
    private void Flash(string msg) { _flash = msg; Invalidate(); }

    // ---------- Tooltip ----------
    private string? _hoverTip;
    private Rectangle _hoverRect;

    private void UpdateHoverTip(Point p)
    {
        string? tip = null;
        Rectangle rect = Rectangle.Empty;
        foreach (var b in _bottomButtons)
            if (b.Rect.Contains(p)) { tip = b.Tip; rect = b.Rect; break; }
        if (tip is null)
            foreach (var b in _sideButtons)
                if (b.Rect.Contains(p)) { tip = b.Tip; rect = b.Rect; break; }

        if (tip != _hoverTip) { _hoverTip = tip; _hoverRect = rect; Invalidate(); }
    }

    private void DrawTooltip(Graphics g)
    {
        if (_hoverTip is null || _hoverRect.IsEmpty) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var f = TooltipFont;
        var sz = g.MeasureString(_hoverTip, f);
        int w = (int)sz.Width + 16, h = 24;
        // acima do botão por padrão; se não couber, abaixo
        int x = _hoverRect.Left + _hoverRect.Width / 2 - w / 2;
        int y = _hoverRect.Top - h - 8;
        if (y < 4) y = _hoverRect.Bottom + 8;
        x = Math.Max(4, Math.Min(x, Width - w - 4));
        var r = new Rectangle(x, y, w, h);
        using (var p = Theme.RoundRect(r, 7))
        using (var b = new SolidBrush(Color.FromArgb(252, 24, 24, 27)))
        using (var pen = new Pen(Theme.Border, 1))
        { g.FillPath(b, p); g.DrawPath(pen, p); }
        using var tb = new SolidBrush(Theme.Text);
        g.DrawString(_hoverTip, f, tb, r, CenterFmt);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_chat.OnMouseWheel(e.Delta, e.Location)) return;
        base.OnMouseWheel(e);
    }

    // ---------- Ferramenta de texto ----------
    private void BeginTextInput(Point at)
    {
        CancelTextInput();
        _textInput = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(24, 24, 27),
            ForeColor = _color,
            Font = new Font("Segoe UI", 9f + _thickness * 3f, FontStyle.Bold),
            Location = at,
            Width = 200,
        };
        _textInput.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitTextInput(); }
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CancelTextInput(); Invalidate(); }
        };
        Controls.Add(_textInput);
        _textInput.Focus();
    }

    private void CommitTextInput()
    {
        if (_textInput is null) return;
        var txt = _textInput.Text;
        var loc = _textInput.Location;
        if (!string.IsNullOrWhiteSpace(txt))
            _shapes.Add(new Shape { Tool = Tool.Text, Color = _color, Thickness = _thickness, A = loc, TextValue = txt });
        CancelTextInput();
        Invalidate();
    }

    private void CancelTextInput()
    {
        if (_textInput is null) return;
        Controls.Remove(_textInput);
        _textInput.Dispose();
        _textInput = null;
    }

    // ---------- Cursor ----------
    private void UpdateCursor(Point p)
    {
        if (_tool != Tool.None) { Cursor = Cursors.Cross; return; }
        var h = SelectionGeometry.HitHandle(_sel, p);
        Cursor = h switch
        {
            ResizeHandle.TL or ResizeHandle.BR => Cursors.SizeNWSE,
            ResizeHandle.TR or ResizeHandle.BL => Cursors.SizeNESW,
            ResizeHandle.T or ResizeHandle.B => Cursors.SizeNS,
            ResizeHandle.L or ResizeHandle.R => Cursors.SizeWE,
            _ => _sel.Contains(p) ? Cursors.SizeAll : Cursors.Default,
        };
    }

    // ---------- Render ----------
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImageUnscaled(_background, 0, 0);

        // escurece tudo, depois clareia a seleção
        using (var dim = new SolidBrush(Theme.Dim)) g.FillRectangle(dim, ClientRectangle);

        if (_sel.Width > 0 && _sel.Height > 0)
        {
            g.SetClip(_sel);
            g.DrawImageUnscaled(_background, 0, 0);
            g.ResetClip();
        }

        // anotações (recortadas à seleção)
        g.SetClip(_sel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var s in _shapes) ShapeRenderer.Draw(g, s);
        if (_drawing is not null) ShapeRenderer.Draw(g, _drawing);
        g.ResetClip();

        if (_mode == Mode.Editing)
        {
            DrawSelectionChrome(g);
            LayoutAndDrawToolbars(g);
            if (_paletteOpen) DrawPalette(g);
            if (_thicknessMenuOpen) DrawThicknessMenu(g);
            if (_chat.IsOpen) _chat.Draw(g, _sel, MonitorBounds(), _sidePanelRect);
        }
        else
        {
            DrawSelectionChrome(g);
        }

        DrawDimensions(g);
        DrawFlash(g);
        if (_mode == Mode.Editing && !_chat.IsOpen) DrawTooltip(g);
    }

    private void DrawSelectionChrome(Graphics g)
    {
        if (_sel.Width <= 0) return;
        using var pen = new Pen(Theme.SelectionStroke, 1.5f);
        g.SmoothingMode = SmoothingMode.None;
        g.DrawRectangle(pen, _sel);

        if (_mode == Mode.Editing)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var hr in SelectionGeometry.HandleRects(_sel))
            {
                using var fill = new SolidBrush(Color.White);
                using var br = new Pen(Color.FromArgb(120, 0, 0, 0), 1);
                using var p = Theme.RoundRect(hr, 2);
                g.FillPath(fill, p);
                g.DrawPath(br, p);
            }
        }
    }

    /// <summary>Bounds (em coords do form/cliente) do monitor que contém a seleção.</summary>
    private Rectangle MonitorBounds()
    {
        var vb = SystemInformation.VirtualScreen;
        var selScreen = new Rectangle(_sel.X + vb.X, _sel.Y + vb.Y, Math.Max(1, _sel.Width), Math.Max(1, _sel.Height));
        var scr = Screen.FromRectangle(selScreen).Bounds;
        return new Rectangle(scr.X - vb.X, scr.Y - vb.Y, scr.Width, scr.Height);
    }

    private void LayoutAndDrawToolbars(Graphics g)
    {
        // Cálculo puro das posições; o overlay só desenha.
        var layout = ToolbarLayout.Compute(_sel, MonitorBounds(), _tool, _paletteOpen, _thicknessMenuOpen);
        _sidePanelRect = layout.SidePanel;
        _sideButtons.Clear(); _sideButtons.AddRange(layout.SideButtons);
        _bottomButtons.Clear(); _bottomButtons.AddRange(layout.BottomButtons);

        // --- Barra lateral (ferramentas) ---
        Theme.DrawPanel(g, layout.SidePanel);
        foreach (var b in _sideButtons)
        {
            DrawIconButton(g, b);
            if (b.Id == "color") // mini swatch da cor atual no canto
            {
                var dot = new Rectangle(b.Rect.Right - 12, b.Rect.Bottom - 12, 7, 7);
                using var sb = new SolidBrush(_color);
                g.FillEllipse(sb, dot);
            }
            else if (b.Id == "thickness")
            {
                DrawThicknessGlyph(g, b.Rect, b.Active ? Color.Black : Theme.Text);
            }
        }

        // --- Barra inferior (ações) ---
        Theme.DrawPanel(g, layout.BottomPanel);
        foreach (var b in _bottomButtons) DrawIconButton(g, b, accentClose: b.Id == "close");
    }

    private void DrawIconButton(Graphics g, IconButton b, bool accentClose = false)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (b.Active)
        {
            using var hl = new SolidBrush(Color.White);
            using var p = Theme.RoundRect(b.Rect, 8);
            g.FillPath(hl, p);
        }
        var color = b.Active ? Color.Black : (accentClose ? Theme.TextMuted : Theme.Text);
        using var br = new SolidBrush(color);
        g.DrawString(b.Glyph, Icons.Cached(20), br, b.Rect, CenterFmt);
    }

    private void DrawPalette(Graphics g)
    {
        _swatches.Clear();
        var colorBtn = _sideButtons.FirstOrDefault(b => b.Id == "color");
        if (colorBtn is null) return;
        int sw = 22, gap = 6, pad = 8, cols = 4;
        int rows = (int)Math.Ceiling(Palette.Length / (double)cols);
        int w = cols * sw + (cols - 1) * gap + pad * 2;
        int h = rows * sw + (rows - 1) * gap + pad * 2;
        int x = colorBtn.Rect.Left - w - 8;
        if (x < 8) x = colorBtn.Rect.Right + 8;
        int y = colorBtn.Rect.Top;
        var panel = new Rectangle(x, y, w, h);
        Theme.DrawPanel(g, panel);
        for (int i = 0; i < Palette.Length; i++)
        {
            int c = i % cols, r = i / cols;
            var rr = new Rectangle(x + pad + c * (sw + gap), y + pad + r * (sw + gap), sw, sw);
            _swatches.Add((rr, Palette[i]));
            using var b = new SolidBrush(Palette[i]);
            using var pth = Theme.RoundRect(rr, 6);
            g.FillPath(b, pth);
            if (Palette[i].ToArgb() == _color.ToArgb())
            {
                using var pen = new Pen(Color.White, 2);
                g.DrawPath(pen, pth);
            }
        }
    }

    /// <summary>Ícone do botão de espessura: 3 barras de peso crescente.</summary>
    private static void DrawThicknessGlyph(Graphics g, Rectangle r, Color color)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int[] w = { 10, 14, 18 };
        int[] barH = { 2, 3, 4 };
        int gap = 3;
        int totalH = barH.Sum() + gap * (barH.Length - 1);
        int cx = r.X + r.Width / 2;
        int y = r.Y + (r.Height - totalH) / 2;
        using var b = new SolidBrush(color);
        for (int i = 0; i < w.Length; i++)
        {
            g.FillRectangle(b, cx - w[i] / 2, y, w[i], barH[i]);
            y += barH[i] + gap;
        }
    }

    /// <summary>Popup com os 3 níveis de espessura (fina/média/grossa) — cada botão mostra um preview da linha.</summary>
    private void DrawThicknessMenu(Graphics g)
    {
        _thicknessSwatches.Clear();
        var btn = _sideButtons.FirstOrDefault(b => b.Id == "thickness");
        if (btn is null) return;
        int bw = 46, bh = 34, gap = 6, pad = 8;
        int w = ThicknessLevels.Length * bw + (ThicknessLevels.Length - 1) * gap + pad * 2;
        int h = bh + pad * 2;
        int x = btn.Rect.Left - w - 8;
        if (x < 8) x = btn.Rect.Right + 8;
        int y = btn.Rect.Top;
        var panel = new Rectangle(x, y, w, h);
        Theme.DrawPanel(g, panel);
        for (int i = 0; i < ThicknessLevels.Length; i++)
        {
            var rr = new Rectangle(x + pad + i * (bw + gap), y + pad, bw, bh);
            _thicknessSwatches.Add((rr, ThicknessLevels[i]));
            bool selected = ThicknessLevels[i] == _thickness;
            using (var p = Theme.RoundRect(rr, 8))
            using (var fill = new SolidBrush(selected ? Color.White : Theme.SurfaceHover))
                g.FillPath(fill, p);
            using var pen = new Pen(selected ? Color.Black : Theme.Text, ThicknessLevels[i]) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            int midY = rr.Y + rr.Height / 2;
            g.DrawLine(pen, rr.X + 10, midY, rr.Right - 10, midY);
        }
    }

    // Recursos GDI reaproveitados no OnPaint (evita alocar por frame).
    private static readonly StringFormat CenterFmt = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    private static readonly Font TooltipFont = new("Segoe UI", 8.5f);
    private static readonly Font DimFont = new("Segoe UI", 9f, FontStyle.Bold);
    private static readonly Font FlashFont = new("Segoe UI", 9.5f);

    private void DrawDimensions(Graphics g)
    {
        if (_sel.Width <= 0) return;
        var txt = $"{_sel.Width} × {_sel.Height}";
        var f = DimFont;
        var sz = g.MeasureString(txt, f);
        int ly = _sel.Top - 24; if (ly < 4) ly = _sel.Top + 6;
        var bg = new Rectangle(_sel.Left, ly, (int)sz.Width + 14, 20);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = Theme.RoundRect(bg, 6))
        using (var b = new SolidBrush(Theme.Surface))
            g.FillPath(b, p);
        using (var tb = new SolidBrush(Theme.Text))
            g.DrawString(txt, f, tb, bg.Left + 7, bg.Top + 3);
    }

    private void DrawFlash(Graphics g)
    {
        if (_flash is null) return;
        var f = FlashFont;
        var sz = g.MeasureString(_flash, f);
        int w = (int)sz.Width + 24;
        // Centraliza no MONITOR PRINCIPAL (não na área virtual de vários monitores).
        var vb = SystemInformation.VirtualScreen;
        var prim = Screen.PrimaryScreen!.Bounds;
        var primClient = new Rectangle(prim.X - vb.X, prim.Y - vb.Y, prim.Width, prim.Height);
        var r = new Rectangle(primClient.Left + (primClient.Width - w) / 2, primClient.Top + 24, w, 30);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = Theme.RoundRect(r, 8))
        using (var b = new SolidBrush(Theme.Surface)) { g.FillPath(b, p); using var pen = new Pen(Theme.Border, 1); g.DrawPath(pen, p); }
        using (var tb = new SolidBrush(Theme.Text))
        {
            g.DrawString(_flash, f, tb, r, CenterFmt);
        }
    }

    /// <summary>Rasteriza a seleção + anotações num novo bitmap.</summary>
    private Bitmap RenderFinal()
    {
        var bmp = new Bitmap(_sel.Width, _sel.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImage(_background, new Rectangle(0, 0, _sel.Width, _sel.Height), _sel, GraphicsUnit.Pixel);
        g.TranslateTransform(-_sel.Left, -_sel.Top);
        foreach (var s in _shapes) ShapeRenderer.Draw(g, s);
        return bmp;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Cancela qualquer chamada de IA/upload em andamento ao fechar.
        try { _cts.Cancel(); } catch { /* já disposto */ }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _cts.Cancel(); } catch { }
            _cts.Dispose();
            _background.Dispose();
        }
        base.Dispose(disposing);
    }
}
