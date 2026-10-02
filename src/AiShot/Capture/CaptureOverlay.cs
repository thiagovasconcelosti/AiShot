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

    /// <summary>
    /// Rotação da moldura, em graus. A seleção continua sendo um retângulo
    /// alinhado aos eixos; este ângulo é aplicado em torno do centro dela.
    /// Quem gira é só a moldura — o conteúdo do print fica reto na tela.
    /// </summary>
    private double _rotacao;

    /// <summary>Ângulo da moldura e do mouse quando o arraste da alça começou.</summary>
    private double _rotacaoNoInicio;
    private double _anguloMouseNoInicio;

    /// <summary>Ponto fixo do redimensionamento em curso (ver SelectionGeometry.Anchor).</summary>
    private PointF _ancora;

    /// <summary>Estado das anotações: formas, ferramenta, cor, espessura e histórico.</summary>
    private readonly AnnotationController _anotacoes = new();

    /// <summary>Percurso do foco do teclado pelos botões das barras.</summary>
    private readonly KeyboardFocus _foco = new();

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
            // Esc devolve o foco antes de fechar: quem navega por teclado pode
            // querer sair do percurso sem abandonar a captura.
            if (_foco.TemFoco) { _foco.Limpar(); Invalidate(); return; }
            Close();
            return;
        }

        // Navegação por teclado nas barras. Só no modo de edição — durante a
        // seleção não há botões — e fora dos campos de texto, onde Tab e Espaço
        // pertencem à digitação.
        if (_mode == Mode.Editing && !EditandoTexto)
        {
            if (e.KeyCode == Keys.Tab)
            {
                AtualizarPercursoDoFoco();
                if (_foco.Mover(paraTras: e.Shift)) { e.SuppressKeyPress = true; Invalidate(); }
                return;
            }

            if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && _foco.Focado is { } alvo)
            {
                e.SuppressKeyPress = true;
                AcionarBotaoFocado(alvo);
                return;
            }
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

        // A moldura pode estar girada: o ponto entra no sistema local (onde a
        // seleção é um retângulo alinhado) e todo o hit-test das alças segue
        // valendo sem matemática própria.
        var local = SelectionGeometry.ToLocal(e.Location, _sel, _rotacao);

        // A alça de rotação fica fora da moldura, acima do meio da aresta
        // superior, e é consultada antes das demais.
        if (SelectionGeometry.HitRotationHandle(_sel, local))
        {
            _activeHandle = ResizeHandle.Rotate;
            _rotacaoNoInicio = _rotacao;
            _anguloMouseNoInicio = AnguloAteO(e.Location);
            _dragging = true;
            return;
        }

        var h = SelectionGeometry.HitHandle(_sel, local);
        if (h != ResizeHandle.None)
        {
            _activeHandle = h;
            _selAtDragStart = _sel;
            _dragStart = e.Location;
            _ancora = SelectionGeometry.Anchor(h, _sel, _rotacao);
            _dragging = true;
            return;
        }

        if (SelectionGeometry.Contains(_sel, local))
        {
            if (_anotacoes.Tool == Tool.None) // mover seleção
            {
                _activeHandle = ResizeHandle.Move;
                _selAtDragStart = _sel;
                _dragStart = e.Location; // a âncora do movimento é o ponto de partida
                _dragging = true;
            }
            else if (_anotacoes.Tool == Tool.Text)
            {
                BeginTextInput(e.Location);
            }
            else if (_anotacoes.Tool == Tool.Step)
            {
                // Posicionado por clique: confirma na hora, sem esperar arraste.
                _anotacoes.BeginDraw(PontoDoConteudo(e.Location));
                _anotacoes.EndDraw();
                Invalidate();
            }
            else // iniciar desenho
            {
                // As formas vivem nas coordenadas do conteúdo (a tela sem giro):
                // desenhadas com a transformação da moldura, elas aparecem sob o
                // cursor e saem giradas junto com o print no arquivo.
                _anotacoes.BeginDraw(PontoDoConteudo(e.Location));
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

            if (_dragging && _activeHandle == ResizeHandle.Rotate)
            {
                Girar(e.Location);
                Invalidate();
                return;
            }
            if (_dragging && _activeHandle != ResizeHandle.None)
            {
                // O movimento anda com o mouse na tela; o redimensionamento é
                // medido a partir da âncora, que fica parada na tela.
                PointF ancora = _activeHandle == ResizeHandle.Move ? _dragStart : _ancora;
                var nova = SelectionGeometry.Resize(_activeHandle, _selAtDragStart, _rotacao, ancora, e.Location);
                if (!nova.IsEmpty) _sel = nova;
                Invalidate();
                return;
            }
            if (_dragging && _anotacoes.InProgress is { } emCurso)
            {
                var ponto = PontoDoConteudo(e.Location);

                // Shift endireita o que está sendo desenhado: reta travada em
                // 0/45/90/135 graus, quadrado no retângulo e círculo na elipse.
                if (ShiftPressionado) ponto = StrokeRegularizer.Restringir(emCurso.Tool, emCurso.A, ponto);

                _anotacoes.ContinueDraw(ponto);
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
            RegularizarSePedido();
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
            case "ocr": _ = _acoes.CopyTextFromImageAsync(_cts.Token); break;
            case "save": _acoes.Save(); break;
            case "paint": _acoes.OpenInPaint(); break;
            case "upload": _ = _acoes.UploadAsync(compartilhar: false, _cts.Token); break;
            case "share": _ = _acoes.UploadAsync(compartilhar: true, _cts.Token); break;
            case "ai": _chat.Open(); break;
            case "close": Close(); break;
        }
    }

    /// <summary>
    /// Árvore de acessibilidade do overlay. Sem isso não há nada que o Narrador
    /// possa anunciar: os botões são pixels desenhados, não controles.
    /// </summary>
    protected override AccessibleObject CreateAccessibilityInstance() =>
        new OverlayAccessibility(
            this,
            botoes: () => _bottomButtons.Concat(_sideButtons).ToArray(),
            focado: () => _foco.Focado,
            acionar: AcionarBotaoFocado,
            origemNaTela: () => SystemInformation.VirtualScreen.Location);

    /// <summary>
    /// Sincroniza o percurso do foco com os botões desenhados no último quadro.
    /// </summary>
    /// <remarks>
    /// As barras são remontadas a cada render e mudam de conteúdo conforme a
    /// paleta abre ou fecha. Atualizar antes de mover garante que Tab caminhe
    /// pelo que está de fato na tela.
    /// </remarks>
    private void AtualizarPercursoDoFoco() =>
        _foco.Atualizar(_bottomButtons.Select(b => b.Id), _sideButtons.Select(b => b.Id));

    /// <summary>Aciona por teclado o botão que está com o foco.</summary>
    private void AcionarBotaoFocado(string id)
    {
        // Os menus suspensos (cor, espessura) precisam do retângulo do botão
        // para se posicionar; os demais ignoram o parâmetro.
        var lateral = _sideButtons.FirstOrDefault(b => b.Id == id);
        if (lateral is not null) { OnSideAction(id, lateral.Rect); return; }

        if (_bottomButtons.Any(b => b.Id == id)) OnBottomAction(id);
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
        // A caixa é um controle e fica alinhada à tela; a forma entra nas
        // coordenadas do conteúdo, para sair girada junto com o print.
        var loc = PontoDoConteudo(_textInput.Location);
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

        var local = SelectionGeometry.ToLocal(p, _sel, _rotacao);
        if (SelectionGeometry.HitRotationHandle(_sel, local)) { Cursor = Cursors.Hand; return; }

        var h = SelectionGeometry.HitHandle(_sel, local);
        Cursor = h == ResizeHandle.None
            ? (SelectionGeometry.Contains(_sel, local) ? Cursors.SizeAll : Cursors.Default)
            : CursorDaAlca(h);
    }

    /// <summary>
    /// Cursor de uma alça de redimensionamento. A direção da alça é girada junto
    /// com a moldura: com ela a 90°, a alça da direita está embaixo e o cursor
    /// tem de ser vertical, senão ele aponta para o lado errado.
    /// </summary>
    private Cursor CursorDaAlca(ResizeHandle alca)
    {
        PointF direcao = alca switch
        {
            ResizeHandle.TL or ResizeHandle.BR => new PointF(1, 1),
            ResizeHandle.TR or ResizeHandle.BL => new PointF(1, -1),
            ResizeHandle.T or ResizeHandle.B => new PointF(0, -1),
            _ => new PointF(1, 0),
        };

        var girada = SelectionGeometry.RotatePoint(direcao, new PointF(0, 0), _rotacao);
        double angulo = Math.Atan2(girada.Y, girada.X) * 180.0 / Math.PI;
        int passo = (int)Math.Round(((angulo % 180) + 180) % 180 / 45.0) % 4;

        return passo switch
        {
            0 => Cursors.SizeWE,
            1 => Cursors.SizeNWSE,
            2 => Cursors.SizeNS,
            _ => Cursors.SizeNESW,
        };
    }

    // ---------- Endireitar traços ----------
    /// <summary>Shift está pressionado? É o gesto que endireita as formas.</summary>
    private static bool ShiftPressionado => (ModifierKeys & Keys.Shift) == Keys.Shift;

    /// <summary>
    /// Com Shift, o traço à mão vira uma forma regular antes de ser confirmado:
    /// linha, círculo ou retângulo, conforme o que o traço desenhou. Rabisco que
    /// não é nenhum dos três continua à mão.
    /// </summary>
    private void RegularizarSePedido()
    {
        if (!ShiftPressionado) return;
        if (_anotacoes.InProgress is not { Tool: Tool.Pen, Points: { } pontos }) return;

        if (StrokeRegularizer.Reconhecer(pontos) is { } regular) _anotacoes.RegularizarEmCurso(regular);
    }

    // ---------- Rotação ----------
    /// <summary>
    /// Ponto do mouse no sistema do conteúdo (a tela sem giro), já arredondado.
    /// É onde as formas e o texto são guardados: desenhados com a transformação da
    /// moldura, eles aparecem sob o cursor e giram junto com o print.
    /// </summary>
    private Point PontoDoConteudo(Point naTela)
    {
        var local = SelectionGeometry.ToLocal(naTela, _sel, _rotacao);
        return new Point((int)Math.Round(local.X), (int)Math.Round(local.Y));
    }

    /// <summary>Ângulo, em graus, do centro da seleção até um ponto da tela.</summary>
    private double AnguloAteO(Point ponto)
    {
        var centro = SelectionGeometry.Center(_sel);
        return Math.Atan2(ponto.Y - centro.Y, ponto.X - centro.X) * 180.0 / Math.PI;
    }

    /// <summary>
    /// Aplica o arraste da alça de rotação: o ângulo anda o mesmo tanto que o
    /// mouse andou em torno do centro, e não salta para debaixo do cursor.
    /// </summary>
    private void Girar(Point ponto) =>
        _rotacao = SelectionGeometry.SnapRotation(
            _rotacaoNoInicio + AnguloAteO(ponto) - _anguloMouseNoInicio,
            ShiftPressionado);

    // ---------- Render ----------
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImageUnscaled(_background, 0, 0);

        // Quando habilitado, escurece tudo e depois clareia a seleção.
        if (!_services.DisableScreenDimming)
            using (var dim = new SolidBrush(Theme.Dim)) g.FillRectangle(dim, ClientRectangle);

        // O print e as anotações giram junto com a moldura, recortados por ela: é
        // o mesmo giro do arquivo exportado, então o que aparece aqui é o que sai
        // lá. As barras e os painéis ficam de fora, alinhados à tela.
        if (_sel.Width > 0 && _sel.Height > 0)
        {
            using var moldura = SelectionGeometry.FramePath(_sel, _rotacao);
            g.SetClip(moldura);

            var centro = SelectionGeometry.Center(_sel);
            g.TranslateTransform(centro.X, centro.Y);
            g.RotateTransform((float)_rotacao);
            g.TranslateTransform(-centro.X, -centro.Y);

            g.DrawImageUnscaled(_background, 0, 0);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            // O borrão lê os pixels de _background, cujas coordenadas coincidem com
            // as do overlay (ambos cobrem a área virtual): deslocamento zero.
            foreach (var s in _anotacoes.Shapes) ShapeRenderer.Draw(g, s, _background);
            if (_anotacoes.InProgress is not null) ShapeRenderer.Draw(g, _anotacoes.InProgress, _background);

            g.ResetTransform();
            g.ResetClip();
        }

        if (_mode == Mode.Editing)
        {
            SelectionChromeRenderer.Draw(g, _sel, _rotacao, comAlcas: true);
            LayoutAndDrawToolbars(g);
            if (_paletteOpen) DrawPalette(g);
            if (_thicknessMenuOpen) DrawThicknessMenu(g);
            if (_chat.IsOpen) _chat.Draw(g, _sel, MonitorBounds(), _sidePanelRect);
        }
        else
        {
            SelectionChromeRenderer.Draw(g, _sel, _rotacao, comAlcas: false);
        }

        _chrome.DrawDimensions(g, _sel, _rotacao);
        _chrome.DrawFlash(g);
        if (_mode == Mode.Editing && !_chat.IsOpen) _chrome.DrawTooltip(g, Width);
    }

    private void DrawSelectionChrome(Graphics g) =>
        SelectionChromeRenderer.Draw(g, _sel, _rotacao, comAlcas: _mode == Mode.Editing);

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

        // O percurso do foco acompanha o que acabou de ser montado: as barras
        // mudam de conteúdo entre quadros e o foco precisa seguir os botões
        // reais, não os do quadro anterior.
        AtualizarPercursoDoFoco();

        ToolbarRenderer.DrawToolbars(g, layout, _anotacoes.Color, _foco.Focado);
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
    private Bitmap RenderFinal() => FinalImageRenderer.Render(_background, _sel, _anotacoes.Shapes, _rotacao);

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
