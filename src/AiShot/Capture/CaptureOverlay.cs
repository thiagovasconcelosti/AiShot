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
    private Rectangle _sel;
    private Point _dragStart;
    private bool _dragging;
    private ResizeHandle _activeHandle = ResizeHandle.None;
    private Rectangle _selAtDragStart;

    /// <summary>Estado das anotações: formas, ferramenta, cor, espessura e histórico.</summary>
    private readonly AnnotationController _anotacoes = new();

    /// <summary>Dica de ferramenta, mensagem transitória e tamanho da seleção.</summary>
    private readonly OverlayChrome _chrome = new();

    /// <summary>Ações da barra inferior (copiar, salvar, Paint, enviar).</summary>
    private readonly OverlayActions _acoes;

    /// <summary>Centralização reaproveitada no desenho dos botões.</summary>
    private static readonly StringFormat CenterFmt =
        new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

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
    // Ferramenta de texto. Vive apenas entre BeginTextInput e Commit/Cancel;
    // CancelTextInput o remove de Controls e o descarta, e o que restar aberto
    // ao fechar a janela é descartado pelo Form junto dos demais filhos.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA2213:Campos descartáveis devem ser descartados",
        Justification = "Controle filho temporário; descartado por CancelTextInput e por Controls.")]
    private TextBox? _textInput;

    public CaptureOverlay(ICaptureServices services)
    {
        _services = services;
        _chat = new ChatPanel(this, StartChatSession, _cts.Token);
        _acoes = new OverlayActions(
            services,
            renderizar: RenderFinal,
            mensagem: Flash,
            fechar: Close,
            descartado: () => IsDisposed);
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

    /// <summary>
    /// true quando algum campo de texto está recebendo digitação — a caixa da
    /// ferramenta de texto ou o campo do chat. Enquanto for verdade, as letras
    /// pertencem ao texto, não aos atalhos de ferramenta.
    /// </summary>
    private bool EditandoTexto => _textInput is not null || _chat.IsOpen;

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
        // Ctrl+Shift+Z e Ctrl+Y refazem: as duas combinações são usuais, e
        // aceitar ambas evita que o usuário precise descobrir qual é a nossa.
        if (e.Control && e.KeyCode == Keys.Z && e.Shift)
        {
            if (_anotacoes.Redo()) Invalidate();
            return;
        }
        if (e.Control && e.KeyCode == Keys.Z)
        {
            if (_anotacoes.Undo()) Invalidate();
            return;
        }
        if (e.Control && e.KeyCode == Keys.Y)
        {
            if (_anotacoes.Redo()) Invalidate();
            return;
        }

        // Atalhos de ferramenta. Só valem no modo de edição e enquanto nenhum
        // campo de texto tem o foco — do contrário, digitar "b" trocaria de
        // ferramenta em vez de escrever.
        if (_mode == Mode.Editing && !e.Control && !e.Alt && !EditandoTexto &&
            _anotacoes.ApplyShortcut((char)e.KeyCode))
        {
            Cursor = _anotacoes.Tool == Tool.None ? Cursors.Default : Cursors.Cross;
            e.SuppressKeyPress = true;
            Invalidate();
            return;
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
                if (r.Contains(e.Location)) { _anotacoes.SetColor(c); _paletteOpen = false; Invalidate(); return; }
            _paletteOpen = false;
        }
        if (_thicknessMenuOpen)
        {
            foreach (var (r, v) in _thicknessSwatches)
                if (r.Contains(e.Location)) { _anotacoes.SetThickness(v); _thicknessMenuOpen = false; Invalidate(); return; }
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
            if (_anotacoes.Tool == Tool.None) // mover seleção
            {
                _activeHandle = ResizeHandle.Move;
                _selAtDragStart = _sel;
                _dragStart = e.Location;
                _dragging = true;
            }
            else if (_anotacoes.Tool == Tool.Text)
            {
                BeginTextInput(e.Location);
            }
            else if (_anotacoes.Tool == Tool.Step)
            {
                // Posicionado por clique: confirma na hora, sem esperar arraste.
                _anotacoes.BeginDraw(e.Location);
                _anotacoes.EndDraw();
                Invalidate();
            }
            else // iniciar desenho
            {
                _anotacoes.BeginDraw(e.Location);
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
            if (_dragging && _anotacoes.InProgress is not null)
            {
                _anotacoes.ContinueDraw(e.Location);
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
            _anotacoes.EndDraw();
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
            case "pen": _anotacoes.ToggleTool(Tool.Pen); break;
            case "arrow": _anotacoes.ToggleTool(Tool.Arrow); break;
            case "line": _anotacoes.ToggleTool(Tool.Line); break;
            case "rect": _anotacoes.ToggleTool(Tool.Rect); break;
            case "ellipse": _anotacoes.ToggleTool(Tool.Ellipse); break;
            case "text": _anotacoes.ToggleTool(Tool.Text); break;
            case "blur": _anotacoes.ToggleTool(Tool.Blur); break;
            case "step": _anotacoes.ToggleTool(Tool.Step); break;
            case "color": _paletteOpen = !_paletteOpen; _thicknessMenuOpen = false; break;
            case "thickness": _thicknessMenuOpen = !_thicknessMenuOpen; _paletteOpen = false; break;
            case "undo": _anotacoes.Undo(); break;
            case "redo": _anotacoes.Redo(); break;
        }
        Cursor = _anotacoes.Tool == Tool.None ? Cursors.Default : Cursors.Cross;
        Invalidate();
    }

    /// <summary>
    /// Abre a sessão de chat sobre um snapshot da seleção. StartChat converte a
    /// imagem em PNG de forma síncrona, então o bitmap pode ser descartado aqui.
    /// </summary>
    private Ai.IAiChatSession StartChatSession()
    {
        using var bmp = RenderFinal();
        return _services.StartChat(bmp);
    }

    private void OnBottomAction(string id)
    {
        switch (id)
        {
            case "copy": _acoes.Copy(); break;
            case "save": _acoes.Save(); break;
            case "paint": _acoes.OpenInPaint(); break;
            case "upload": _ = _acoes.UploadAsync(compartilhar: false, _cts.Token); break;
            case "share": _ = _acoes.UploadAsync(compartilhar: true, _cts.Token); break;
            case "ai": _chat.Open(); break;
            case "close": Close(); break;
        }
    }

    private void Flash(string msg) { _chrome.Flash(msg); Invalidate(); }

    private void UpdateHoverTip(Point p)
    {
        // A barra inferior é consultada primeiro: quando as duas se aproximam,
        // é ela que fica por cima.
        if (_chrome.UpdateHover(p, _bottomButtons, _sideButtons)) Invalidate();
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
            ForeColor = _anotacoes.Color,
            Font = new Font("Segoe UI", 9f + _anotacoes.Thickness * 3f, FontStyle.Bold),
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
            _anotacoes.Add(new Shape { Tool = Tool.Text, Color = _anotacoes.Color, Thickness = _anotacoes.Thickness, A = loc, TextValue = txt });
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
        if (_anotacoes.Tool != Tool.None) { Cursor = Cursors.Cross; return; }
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
        // O borrão lê os pixels de _background, cujas coordenadas coincidem com
        // as do overlay (ambos cobrem a área virtual): deslocamento zero.
        foreach (var s in _anotacoes.Shapes) ShapeRenderer.Draw(g, s, _background);
        if (_anotacoes.InProgress is not null) ShapeRenderer.Draw(g, _anotacoes.InProgress, _background);
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

        _chrome.DrawDimensions(g, _sel);
        _chrome.DrawFlash(g);
        if (_mode == Mode.Editing && !_chat.IsOpen) _chrome.DrawTooltip(g, Width);
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
        // Cálculo puro das posições; o desenho fica no ToolbarRenderer.
        var layout = ToolbarLayout.Compute(_sel, MonitorBounds(), _anotacoes.Tool, _paletteOpen, _thicknessMenuOpen);
        _sidePanelRect = layout.SidePanel;
        _sideButtons.Clear(); _sideButtons.AddRange(layout.SideButtons);
        _bottomButtons.Clear(); _bottomButtons.AddRange(layout.BottomButtons);

        ToolbarRenderer.DrawToolbars(g, layout, _anotacoes.Color);
    }

    private void DrawPalette(Graphics g)
    {
        _swatches.Clear();
        _swatches.AddRange(ToolbarRenderer.DrawPalette(g, _sideButtons, _anotacoes.Color));
    }

    private void DrawThicknessMenu(Graphics g)
    {
        _thicknessSwatches.Clear();
        _thicknessSwatches.AddRange(ToolbarRenderer.DrawThicknessMenu(g, _sideButtons, _anotacoes.Thickness));
    }

    /// <summary>Rasteriza a seleção + anotações num novo bitmap.</summary>
    private Bitmap RenderFinal()
    {
        var bmp = new Bitmap(_sel.Width, _sel.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImage(_background, new Rectangle(0, 0, _sel.Width, _sel.Height), _sel, GraphicsUnit.Pixel);

        // A partir daqui o Graphics converte coordenadas do overlay para as do
        // bitmap; os shapes seguem sendo desenhados em coordenadas do overlay,
        // que são as mesmas de _background — daí o deslocamento zero na leitura
        // dos pixels pelo borrão.
        g.TranslateTransform(-_sel.Left, -_sel.Top);
        foreach (var s in _anotacoes.Shapes) ShapeRenderer.Draw(g, s, _background);
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
