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
    private enum Tool { None, Pen, Arrow, Line, Rect, Ellipse, Text }

    private enum Handle { None, TL, T, TR, R, BR, B, BL, L, Move }

    private sealed class Shape
    {
        public Tool Tool;
        public Color Color;
        public int Thickness;
        public Point A, B;
        public List<Point>? Points;   // pen
        public string? TextValue;     // text
    }

    private sealed record IconButton(Rectangle Rect, string Glyph, string Id, bool Active, string Tip);

    private readonly Bitmap _background;
    private readonly ICaptureServices _services;

    private Mode _mode = Mode.Selecting;
    private Tool _tool = Tool.None;
    private Rectangle _sel;
    private Point _dragStart;
    private bool _dragging;
    private Handle _activeHandle = Handle.None;
    private Rectangle _selAtDragStart;

    private readonly List<Shape> _shapes = new();
    private Shape? _drawing;
    private Color _color = Color.FromArgb(239, 68, 68); // vermelho (padrão, igual referência)
    private int _thickness = 3;

    // Layout recalculado a cada render
    private readonly List<IconButton> _sideButtons = new();
    private readonly List<IconButton> _bottomButtons = new();
    private readonly List<(Rectangle r, Color c)> _swatches = new();
    private bool _paletteOpen;
    private Rectangle _sidePanelRect;

    // Chat
    private bool _chatOpen;
    private bool _chatBusy;
    private Ai.IAiChatSession? _session;
    private readonly List<(string role, string text)> _messages = new();
    private Rectangle _chatBubble, _chatSendBtn, _chatViewport;
    private int _chatScroll;
    private int _chatContentHeight;
    private TextBox? _chatInput;
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
            if (_chatOpen) { CloseChat(); Invalidate(); return; }
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
        if (_chatOpen)
        {
            if (_chatSendBtn.Contains(e.Location)) { _ = SendChatAsync(); return; }
            if (_chatBubble.Contains(e.Location)) return; // clique dentro do balão
        }
        if (_paletteOpen)
        {
            foreach (var (r, c) in _swatches)
                if (r.Contains(e.Location)) { _color = c; _paletteOpen = false; Invalidate(); return; }
            _paletteOpen = false;
        }
        foreach (var b in _bottomButtons)
            if (b.Rect.Contains(e.Location)) { OnBottomAction(b.Id); return; }
        foreach (var b in _sideButtons)
            if (b.Rect.Contains(e.Location)) { OnSideAction(b.Id, b.Rect); return; }

        var h = HitHandle(e.Location);
        if (h != Handle.None)
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
                _activeHandle = Handle.Move;
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
            _sel = Normalize(_dragStart, e.Location);
            Invalidate();
            return;
        }

        if (_mode == Mode.Editing)
        {
            UpdateCursor(e.Location);
            UpdateHoverTip(e.Location);

            if (_dragging && _activeHandle != Handle.None)
            {
                ResizeOrMove(e.Location);
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
                if (IsShapeValid(_drawing)) _shapes.Add(_drawing);
                _drawing = null;
            }
            _activeHandle = Handle.None;
            ClampSelection();
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
            case "color": _paletteOpen = !_paletteOpen; break;
            case "undo": if (_shapes.Count > 0) _shapes.RemoveAt(_shapes.Count - 1); break;
        }
        Cursor = _tool == Tool.None ? Cursors.Default : Cursors.Cross;
        Invalidate();
    }

    private Tool Toggle(Tool t) => _tool == t ? Tool.None : t;

    private async void OnBottomAction(string id)
    {
        switch (id)
        {
            case "copy": _services.CopyToClipboard(RenderFinal()); Flash("Copiado"); break;
            case "save": _services.SaveToFile(RenderFinal()); break;
            case "paint": OpenInPaint(); break;
            case "upload": await DoUploadAsync(); break;
            case "share": await DoUploadAsync(share: true); break;
            case "ai": OpenChat(); break;
            case "close": Close(); break;
        }
    }

    private async Task DoUploadAsync(bool share = false)
    {
        try
        {
            Flash("Enviando…");
            var url = await _services.UploadAsync(RenderFinal());
            Clipboard.SetText(url);
            if (share)
            {
                // Compartilhar: abre a URL no navegador padrão.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
                Flash("Aberto no navegador (URL copiada)");
            }
            else
            {
                Flash("URL copiada: " + url);
            }
        }
        catch (Exception ex) { Flash(share ? "Falha ao compartilhar: " + ex.Message : "Falha no upload: " + ex.Message); }
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
        using var f = new Font("Segoe UI", 8.5f);
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
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(_hoverTip, f, tb, r, fmt);
    }

    // ---------- Chat IA ----------
    private void OpenChat()
    {
        _chatOpen = true;
        // Sessão contínua: snapshot da imagem (com anotações) no momento da abertura.
        _session ??= _services.StartChat(RenderFinal());
        EnsureChatInput();
        LayoutChat();
        _chatInput!.Visible = true;
        _chatInput.Focus();
        Invalidate();
    }

    private void CloseChat()
    {
        _chatOpen = false;
        if (_chatInput is not null) _chatInput.Visible = false;
    }

    private void EnsureChatInput()
    {
        if (_chatInput is not null) return;
        _chatInput = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(24, 24, 27),
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10.5f),
            Multiline = false,
        };
        _chatInput.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = SendChatAsync(); }
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CloseChat(); Invalidate(); }
        };
        Controls.Add(_chatInput);
    }

    private async Task SendChatAsync()
    {
        if (_chatBusy || _chatInput is null || _session is null) return;
        var q = _chatInput.Text.Trim();
        if (q.Length == 0) return;

        _messages.Add(("user", q));
        _chatInput.Clear();
        _chatBusy = true;
        _chatInput.Enabled = false;
        _scrollToBottom = true;
        Invalidate();
        try
        {
            var ans = await _session.SendAsync(q);
            _messages.Add(("assistant", ans));
        }
        catch (Exception ex) { _messages.Add(("assistant", "Erro: " + ex.Message)); }
        finally
        {
            _chatBusy = false;
            _chatInput.Enabled = true;
            _chatInput.Focus();
            _scrollToBottom = true;
            Invalidate();
        }
    }

    private bool _scrollToBottom;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_chatOpen && _chatViewport.Contains(e.Location))
        {
            int max = Math.Max(0, _chatContentHeight - _chatViewport.Height);
            _chatScroll = Math.Max(0, Math.Min(max, _chatScroll - e.Delta));
            Invalidate();
            return;
        }
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

    // ---------- Geometria seleção ----------
    private static Rectangle Normalize(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    private Rectangle[] HandleRects()
    {
        int s = 9, h = s / 2;
        var r = _sel;
        Point[] pts =
        {
            new(r.Left, r.Top), new(r.Left + r.Width / 2, r.Top), new(r.Right, r.Top),
            new(r.Right, r.Top + r.Height / 2), new(r.Right, r.Bottom),
            new(r.Left + r.Width / 2, r.Bottom), new(r.Left, r.Bottom), new(r.Left, r.Top + r.Height / 2),
        };
        return pts.Select(p => new Rectangle(p.X - h, p.Y - h, s, s)).ToArray();
    }

    private Handle HitHandle(Point p)
    {
        var rects = HandleRects();
        Handle[] order = { Handle.TL, Handle.T, Handle.TR, Handle.R, Handle.BR, Handle.B, Handle.BL, Handle.L };
        for (int i = 0; i < rects.Length; i++)
            if (rects[i].Contains(p)) return order[i];
        return Handle.None;
    }

    private void ResizeOrMove(Point now)
    {
        int dx = now.X - _dragStart.X, dy = now.Y - _dragStart.Y;
        var r = _selAtDragStart;
        switch (_activeHandle)
        {
            case Handle.Move: r.Offset(dx, dy); break;
            case Handle.TL: r = Rectangle.FromLTRB(r.Left + dx, r.Top + dy, r.Right, r.Bottom); break;
            case Handle.T: r = Rectangle.FromLTRB(r.Left, r.Top + dy, r.Right, r.Bottom); break;
            case Handle.TR: r = Rectangle.FromLTRB(r.Left, r.Top + dy, r.Right + dx, r.Bottom); break;
            case Handle.R: r = Rectangle.FromLTRB(r.Left, r.Top, r.Right + dx, r.Bottom); break;
            case Handle.BR: r = Rectangle.FromLTRB(r.Left, r.Top, r.Right + dx, r.Bottom + dy); break;
            case Handle.B: r = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom + dy); break;
            case Handle.BL: r = Rectangle.FromLTRB(r.Left + dx, r.Top, r.Right, r.Bottom + dy); break;
            case Handle.L: r = Rectangle.FromLTRB(r.Left + dx, r.Top, r.Right, r.Bottom); break;
        }
        if (r.Width >= 16 && r.Height >= 16) _sel = r;
    }

    private void ClampSelection()
    {
        var r = _sel;
        r.X = Math.Max(0, Math.Min(r.X, Width - r.Width));
        r.Y = Math.Max(0, Math.Min(r.Y, Height - r.Height));
        _sel = r;
    }

    private void UpdateCursor(Point p)
    {
        if (_tool != Tool.None) { Cursor = Cursors.Cross; return; }
        var h = HitHandle(p);
        Cursor = h switch
        {
            Handle.TL or Handle.BR => Cursors.SizeNWSE,
            Handle.TR or Handle.BL => Cursors.SizeNESW,
            Handle.T or Handle.B => Cursors.SizeNS,
            Handle.L or Handle.R => Cursors.SizeWE,
            _ => _sel.Contains(p) ? Cursors.SizeAll : Cursors.Default,
        };
    }

    private static bool IsShapeValid(Shape s) => s.Tool switch
    {
        Tool.Pen => s.Points is { Count: > 1 },
        _ => Math.Abs(s.A.X - s.B.X) > 2 || Math.Abs(s.A.Y - s.B.Y) > 2,
    };

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
        foreach (var s in _shapes) DrawShape(g, s);
        if (_drawing is not null) DrawShape(g, _drawing);
        g.ResetClip();

        if (_mode == Mode.Editing)
        {
            DrawSelectionChrome(g);
            LayoutAndDrawToolbars(g);
            if (_paletteOpen) DrawPalette(g);
            if (_chatOpen) DrawChat(g);
        }
        else
        {
            DrawSelectionChrome(g);
        }

        DrawDimensions(g);
        DrawFlash(g);
        if (_mode == Mode.Editing && !_chatOpen) DrawTooltip(g);
    }

    private void DrawShape(Graphics g, Shape s)
    {
        using var pen = new Pen(s.Color, s.Thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (s.Tool)
        {
            case Tool.Pen:
                if (s.Points is { Count: > 1 }) g.DrawLines(pen, s.Points.ToArray());
                break;
            case Tool.Line:
                g.DrawLine(pen, s.A, s.B);
                break;
            case Tool.Arrow:
                pen.CustomEndCap = new AdjustableArrowCap(4, 5);
                g.DrawLine(pen, s.A, s.B);
                break;
            case Tool.Rect:
                g.DrawRectangle(pen, Normalize(s.A, s.B));
                break;
            case Tool.Ellipse:
                g.DrawEllipse(pen, Normalize(s.A, s.B));
                break;
            case Tool.Text:
                using (var f = new Font("Segoe UI", 9f + s.Thickness * 3f, FontStyle.Bold))
                using (var b = new SolidBrush(s.Color))
                    g.DrawString(s.TextValue, f, b, s.A);
                break;
        }
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
            foreach (var hr in HandleRects())
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
        _sideButtons.Clear();
        _bottomButtons.Clear();
        var mon = MonitorBounds();

        // --- Barra lateral (ferramentas de desenho) ---
        (string glyph, string id, string tip, Tool t)[] tools =
        {
            (Icons.Pencil, "pen", "Lápis", Tool.Pen),
            (Icons.Arrow, "arrow", "Seta", Tool.Arrow),
            (Icons.Line, "line", "Linha", Tool.Line),
            (Icons.Rectangle, "rect", "Retângulo", Tool.Rect),
            (Icons.Circle, "ellipse", "Elipse", Tool.Ellipse),
            (Icons.Text, "text", "Texto", Tool.Text),
            (Icons.Palette, "color", "Cor", Tool.None),
            (Icons.Undo, "undo", "Desfazer", Tool.None),
        };
        int bs = Theme.ButtonSize, gap = 2, pad = Theme.BarPad;
        int panelW = bs + pad * 2;
        int panelH = tools.Length * bs + (tools.Length - 1) * gap + pad * 2;
        int sx = _sel.Right + 12;
        if (sx + panelW > mon.Right - 8) sx = _sel.Left - 12 - panelW;
        if (sx < mon.Left + 8) sx = mon.Right - 8 - panelW; // último recurso
        int sy = Math.Max(mon.Top + 8, Math.Min(_sel.Top, mon.Bottom - panelH - 8));
        var sidePanel = new Rectangle(sx, sy, panelW, panelH);
        _sidePanelRect = sidePanel;
        Theme.DrawPanel(g, sidePanel);
        for (int i = 0; i < tools.Length; i++)
        {
            var r = new Rectangle(sx + pad, sy + pad + i * (bs + gap), bs, bs);
            bool active = tools[i].id == "color" ? _paletteOpen : (_tool == tools[i].t && tools[i].t != Tool.None);
            _sideButtons.Add(new IconButton(r, tools[i].glyph, tools[i].id, active, tools[i].tip));
        }
        foreach (var b in _sideButtons)
        {
            DrawIconButton(g, b);
            if (b.Id == "color") // mini swatch da cor atual no canto
            {
                var dot = new Rectangle(b.Rect.Right - 12, b.Rect.Bottom - 12, 7, 7);
                using var sb = new SolidBrush(_color);
                g.FillEllipse(sb, dot);
            }
        }

        // --- Barra inferior (ações) ---
        (string glyph, string id, string tip)[] actions =
        {
            (Icons.Copy, "copy", "Copiar"),
            (Icons.Save, "save", "Salvar"),
            (Icons.Paint, "paint", "Abrir no Paint"),
            (Icons.Upload, "upload", "Upload"),
            (Icons.Share, "share", "Compartilhar"),
            (Icons.Chat, "ai", "Perguntar à IA"),
            (Icons.Close, "close", "Fechar"),
        };
        int bw = actions.Length * bs + (actions.Length - 1) * gap + pad * 2;
        int bx = _sel.Left + (_sel.Width - bw) / 2;
        bx = Math.Max(mon.Left + 8, Math.Min(bx, mon.Right - bw - 8));
        int by = _sel.Bottom + 12;
        if (by + bs + pad * 2 > mon.Bottom - 8) by = _sel.Top - 12 - (bs + pad * 2);
        var botPanel = new Rectangle(bx, by, bw, bs + pad * 2);

        // Evita sobrepor a toolbar lateral: empurra pro lado oposto a ela.
        if (botPanel.IntersectsWith(_sidePanelRect))
        {
            bool sideRight = _sidePanelRect.Left >= _sel.Right;
            if (sideRight) bx = _sidePanelRect.Left - 8 - bw;
            else bx = _sidePanelRect.Right + 8;
            bx = Math.Max(mon.Left + 8, Math.Min(bx, mon.Right - bw - 8));
            botPanel = new Rectangle(bx, by, bw, bs + pad * 2);
        }
        Theme.DrawPanel(g, botPanel);
        for (int i = 0; i < actions.Length; i++)
        {
            var r = new Rectangle(bx + pad + i * (bs + gap), by + pad, bs, bs);
            _bottomButtons.Add(new IconButton(r, actions[i].glyph, actions[i].id, false, actions[i].tip));
        }
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
        using var f = Icons.Font(20);
        using var br = new SolidBrush(color);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(b.Glyph, f, br, b.Rect, sf);
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

    private static readonly Font ChatFont = new("Segoe UI", 10f);

    private void DrawChat(Graphics g)
    {
        LayoutChat();
        Theme.DrawPanel(g, _chatBubble);
        var inner = Rectangle.Inflate(_chatBubble, -14, -14);

        // --- cabeçalho ---
        using (var hf = Icons.Font(18))
        using (var hb = new SolidBrush(Theme.Text))
            g.DrawString(Icons.Sparkle, hf, hb, inner.Left, inner.Top - 2);
        using (var tf = new Font("Segoe UI Semibold", 10.5f))
        using (var tb = new SolidBrush(Theme.Text))
            g.DrawString("Perguntar à IA", tf, tb, inner.Left + 26, inner.Top);
        using (var sep = new Pen(Theme.BorderSubtle, 1))
            g.DrawLine(sep, inner.Left, inner.Top + 26, inner.Right, inner.Top + 26);

        // --- viewport rolável (timeline) ---
        int inputH = 34;
        _chatViewport = new Rectangle(inner.Left, inner.Top + 34, inner.Width, inner.Bottom - inputH - 8 - (inner.Top + 34));

        var oldClip = g.Clip;
        g.SetClip(_chatViewport);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int maxBubbleW = (int)(_chatViewport.Width * 0.78);
        int y = _chatViewport.Top - _chatScroll;
        const int gap = 8, padX = 11, padY = 8;

        foreach (var (role, text) in _messages)
        {
            bool user = role == "user";
            var sz = g.MeasureString(text, ChatFont, maxBubbleW - padX * 2);
            int bw = (int)Math.Ceiling(sz.Width) + padX * 2;
            int bh = (int)Math.Ceiling(sz.Height) + padY * 2;
            int bx = user ? _chatViewport.Right - bw : _chatViewport.Left;
            var bub = new Rectangle(bx, y, bw, bh);

            using (var p = Theme.RoundRect(bub, 10))
            using (var fill = new SolidBrush(user ? Color.White : Theme.SurfaceHover))
                g.FillPath(fill, p);
            using (var tb = new SolidBrush(user ? Color.Black : Theme.Text))
                g.DrawString(text, ChatFont, tb, new RectangleF(bub.X + padX, bub.Y + padY, maxBubbleW - padX * 2, bh));

            y += bh + gap;
        }

        // indicador "digitando…"
        if (_chatBusy)
        {
            var bub = new Rectangle(_chatViewport.Left, y, 70, 30);
            using (var p = Theme.RoundRect(bub, 10))
            using (var fill = new SolidBrush(Theme.SurfaceHover))
                g.FillPath(fill, p);
            using (var tb = new SolidBrush(Theme.TextMuted))
                g.DrawString("• • •", ChatFont, tb, bub.X + padX, bub.Y + padY);
            y += 30 + gap;
        }

        _chatContentHeight = (y + _chatScroll) - _chatViewport.Top;

        if (_messages.Count == 0 && !_chatBusy)
        {
            using var ph = new SolidBrush(Theme.TextMuted);
            g.DrawString("Pergunte algo sobre o print…", ChatFont, ph, _chatViewport.Left, _chatViewport.Top + 4);
        }

        g.Clip = oldClip;

        // auto-scroll pro fim
        if (_scrollToBottom)
        {
            _chatScroll = Math.Max(0, _chatContentHeight - _chatViewport.Height);
            _scrollToBottom = false;
        }

        // --- input + enviar ---
        var inputRow = new Rectangle(inner.Left, inner.Bottom - inputH, inner.Width, inputH);
        var sendBtn = new Rectangle(inputRow.Right - 32, inputRow.Top + 2, 30, 30);
        _chatSendBtn = sendBtn;

        using (var ip = Theme.RoundRect(new Rectangle(inputRow.Left, inputRow.Top, inputRow.Width - 38, inputH), 9))
        using (var ifill = new SolidBrush(Color.FromArgb(24, 24, 27)))
        using (var ipen = new Pen(Theme.Border, 1))
        { g.FillPath(ifill, ip); g.DrawPath(ipen, ip); }

        if (_chatInput is not null)
            _chatInput.Bounds = new Rectangle(inputRow.Left + 10, inputRow.Top + 8, inputRow.Width - 38 - 18, 20);

        using (var p = Theme.RoundRect(sendBtn, 9))
        using (var bb = new SolidBrush(_chatBusy ? Theme.SurfaceHover : Color.White))
            g.FillPath(bb, p);
        using (var sf = Icons.Font(16))
        using (var sb = new SolidBrush(_chatBusy ? Theme.TextMuted : Color.Black))
        {
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(Icons.Send, sf, sb, sendBtn, fmt);
        }
    }

    private void LayoutChat()
    {
        var mon = MonitorBounds();
        const int w = 360, h = 340, m = 12;

        // Considera a toolbar lateral ocupando um dos lados da seleção.
        bool sideOnRight = !_sidePanelRect.IsEmpty && _sidePanelRect.Left >= _sel.Right;
        bool sideOnLeft = !_sidePanelRect.IsEmpty && _sidePanelRect.Right <= _sel.Left;
        int rightEdge = sideOnRight ? _sidePanelRect.Right : _sel.Right;
        int leftEdge = sideOnLeft ? _sidePanelRect.Left : _sel.Left;

        // y/x alinhados e clampados ao monitor
        int yAligned = Math.Max(mon.Top + 8, Math.Min(_sel.Top, mon.Bottom - h - 8));
        int xCentered = Math.Max(mon.Left + 8, Math.Min(_sel.Left + (_sel.Width - w) / 2, mon.Right - w - 8));

        // Candidatos em ordem de preferência (depois da marcação, sem cobrir o print).
        var candidates = new[]
        {
            new Rectangle(rightEdge + m, yAligned, w, h),          // direita
            new Rectangle(leftEdge - m - w, yAligned, w, h),       // esquerda
            new Rectangle(xCentered, _sel.Bottom + m, w, h),       // abaixo
            new Rectangle(xCentered, _sel.Top - m - h, w, h),      // acima
        };

        foreach (var c in candidates)
        {
            bool insideMonitor = c.Left >= mon.Left + 4 && c.Top >= mon.Top + 4 &&
                                 c.Right <= mon.Right - 4 && c.Bottom <= mon.Bottom - 4;
            if (insideMonitor && !c.IntersectsWith(_sel)) { _chatBubble = c; return; }
        }

        // Sem espaço livre: sobrepõe, mas sempre dentro do monitor.
        int fx = Math.Max(mon.Left + 8, Math.Min(_sel.Right - w, mon.Right - w - 8));
        int fy = Math.Max(mon.Top + 8, Math.Min(_sel.Top, mon.Bottom - h - 8));
        _chatBubble = new Rectangle(fx, fy, w, h);
    }

    private void DrawDimensions(Graphics g)
    {
        if (_sel.Width <= 0) return;
        var txt = $"{_sel.Width} × {_sel.Height}";
        using var f = new Font("Segoe UI", 9f, FontStyle.Bold);
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
        using var f = new Font("Segoe UI", 9.5f);
        var sz = g.MeasureString(_flash, f);
        int w = (int)sz.Width + 24;
        var r = new Rectangle((Width - w) / 2, 24, w, 30);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = Theme.RoundRect(r, 8))
        using (var b = new SolidBrush(Theme.Surface)) { g.FillPath(b, p); using var pen = new Pen(Theme.Border, 1); g.DrawPath(pen, p); }
        using (var tb = new SolidBrush(Theme.Text))
        {
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(_flash, f, tb, r, fmt);
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
        foreach (var s in _shapes) DrawShape(g, s);
        return bmp;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _background.Dispose();
        base.Dispose(disposing);
    }
}
