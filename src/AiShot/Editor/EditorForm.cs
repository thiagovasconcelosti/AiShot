using System.Drawing.Drawing2D;
using AiShot.Capture;

namespace AiShot.Editor;

/// <summary>
/// Editor de anotação estilo Lightshot/prntscr.
/// Mostra a captura num canvas e permite desenhar setas, retângulos, elipses,
/// linhas, traço livre e texto por cima. As anotações são vetoriais (lista de
/// shapes redesenhada no Paint), com suporte a Undo (Ctrl+Z).
/// A toolbar de ações consome os <see cref="ICaptureServices"/> injetados.
/// </summary>
public sealed class EditorForm : Form
{
    // ----- Modelo de anotações (records/classes internos) -----

    /// <summary>Tipos de ferramenta de desenho disponíveis.</summary>
    private enum ToolType { Arrow, Rectangle, Ellipse, Line, Pen, Text }

    /// <summary>Contrato base de qualquer anotação desenhável.</summary>
    private abstract class Shape
    {
        public Color Color { get; init; }
        public float Thickness { get; init; }
        public abstract void Draw(Graphics g);
    }

    /// <summary>Anotação definida por dois pontos (início/fim): retângulo, elipse, linha, seta.</summary>
    private abstract class TwoPointShape : Shape
    {
        public Point Start { get; set; }
        public Point End { get; set; }

        /// <summary>Retângulo normalizado (lida com arraste em qualquer direção).</summary>
        protected Rectangle Bounds()
        {
            int x = Math.Min(Start.X, End.X);
            int y = Math.Min(Start.Y, End.Y);
            int w = Math.Abs(Start.X - End.X);
            int h = Math.Abs(Start.Y - End.Y);
            return new Rectangle(x, y, w, h);
        }
    }

    private sealed class RectShape : TwoPointShape
    {
        public override void Draw(Graphics g)
        {
            using var pen = new Pen(Color, Thickness);
            g.DrawRectangle(pen, Bounds());
        }
    }

    private sealed class EllipseShape : TwoPointShape
    {
        public override void Draw(Graphics g)
        {
            using var pen = new Pen(Color, Thickness);
            g.DrawEllipse(pen, Bounds());
        }
    }

    private sealed class LineShape : TwoPointShape
    {
        public override void Draw(Graphics g)
        {
            using var pen = new Pen(Color, Thickness);
            g.DrawLine(pen, Start, End);
        }
    }

    /// <summary>Linha com ponta de seta (triângulo) na extremidade final.</summary>
    private sealed class ArrowShape : TwoPointShape
    {
        public override void Draw(Graphics g)
        {
            using var pen = new Pen(Color, Thickness)
            {
                // Ponta de seta proporcional à espessura.
                CustomEndCap = new AdjustableArrowCap(3f + Thickness, 3f + Thickness)
            };
            g.DrawLine(pen, Start, End);
        }
    }

    /// <summary>Traço livre (lápis): sequência de pontos.</summary>
    private sealed class PenShape : Shape
    {
        public List<Point> Points { get; } = new();
        public override void Draw(Graphics g)
        {
            if (Points.Count < 2) return;
            using var pen = new Pen(Color, Thickness)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            g.DrawLines(pen, Points.ToArray());
        }
    }

    /// <summary>Texto posicionado num ponto.</summary>
    private sealed class TextShape : Shape
    {
        public Point Location { get; init; }
        public string Text { get; init; } = "";
        public override void Draw(Graphics g)
        {
            // Tamanho da fonte derivado da espessura selecionada.
            float emSize = 10f + Thickness * 4f;
            using var font = new Font(FontFamily.GenericSansSerif, emSize, FontStyle.Bold);
            using var brush = new SolidBrush(Color);
            g.DrawString(Text, font, brush, Location);
        }
    }

    // ----- Estado -----

    private readonly Bitmap _capture;            // imagem base capturada
    private readonly ICaptureServices _services;

    private readonly List<Shape> _shapes = new();
    private Shape? _current;                      // shape em construção (arraste atual)

    private ToolType _tool = ToolType.Arrow;
    private Color _color = Color.Red;
    private float _thickness = 3f;                // média por padrão

    // Controles
    private readonly PictureBox _canvas = new();
    private readonly Button _btnColor = new();
    private readonly Panel _aiPanel = new();
    private readonly TextBox _aiOutput = new();
    private readonly Button _btnUpload = new();
    private readonly Button _btnAsk = new();

    public EditorForm(Bitmap capture, ICaptureServices services)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _services = services ?? throw new ArgumentNullException(nameof(services));

        // ----- Form -----
        Text = "AiShot — Editor";
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true; // para capturar Esc/Ctrl+Z globalmente
        DoubleBuffered = true;
        // Tamanho inicial folgado, limitado à imagem + toolbar.
        ClientSize = new Size(
            Math.Max(720, Math.Min(_capture.Width + 24, 1280)),
            Math.Max(480, Math.Min(_capture.Height + 140, 900)));

        BuildToolbar(out var toolbar);
        BuildCanvas();
        BuildAiPanel();

        // Ordem de docking: toolbar no topo, painel IA embaixo, canvas preenche o meio.
        Controls.Add(_canvas);
        Controls.Add(_aiPanel);
        Controls.Add(toolbar);

        KeyDown += OnKeyDown;
    }

    // ----- Construção da UI -----

    private void BuildToolbar(out FlowLayoutPanel toolbar)
    {
        toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(4),
            BackColor = SystemColors.Control
        };

        // --- Ferramentas de desenho ---
        AddToolButton(toolbar, "Seta", ToolType.Arrow);
        AddToolButton(toolbar, "Retângulo", ToolType.Rectangle);
        AddToolButton(toolbar, "Elipse", ToolType.Ellipse);
        AddToolButton(toolbar, "Linha", ToolType.Line);
        AddToolButton(toolbar, "Lápis", ToolType.Pen);
        AddToolButton(toolbar, "Texto", ToolType.Text);

        toolbar.Controls.Add(new Label { Text = " | ", AutoSize = true, Padding = new Padding(4, 8, 4, 0) });

        // --- Paleta de cores ---
        foreach (var c in new[] { Color.Red, Color.Lime, Color.Blue, Color.Yellow, Color.Black, Color.White })
            AddColorSwatch(toolbar, c);

        // Botão de cor customizada (ColorDialog).
        _btnColor.Text = "Cor...";
        _btnColor.AutoSize = true;
        _btnColor.BackColor = _color;
        _btnColor.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _color, FullOpen = true };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                SetColor(dlg.Color);
        };
        toolbar.Controls.Add(_btnColor);

        toolbar.Controls.Add(new Label { Text = " | ", AutoSize = true, Padding = new Padding(4, 8, 4, 0) });

        // --- Espessura ---
        AddThicknessButton(toolbar, "Fina", 1.5f);
        AddThicknessButton(toolbar, "Média", 3f);
        AddThicknessButton(toolbar, "Grossa", 6f);

        toolbar.Controls.Add(new Label { Text = " | ", AutoSize = true, Padding = new Padding(4, 8, 4, 0) });

        // --- Ações ---
        AddActionButton(toolbar, "Copiar", OnCopy);
        AddActionButton(toolbar, "Salvar", OnSave);

        _btnUpload.Text = "Upload";
        _btnUpload.AutoSize = true;
        _btnUpload.Click += async (_, _) => await OnUploadAsync();
        toolbar.Controls.Add(_btnUpload);

        _btnAsk.Text = "Perguntar à IA";
        _btnAsk.AutoSize = true;
        _btnAsk.Click += async (_, _) => await OnAskAiAsync();
        toolbar.Controls.Add(_btnAsk);

        AddActionButton(toolbar, "Desfazer", (_, _) => Undo());
    }

    private void AddToolButton(FlowLayoutPanel bar, string text, ToolType tool)
    {
        var b = new Button { Text = text, AutoSize = true, Tag = tool };
        b.Click += (_, _) => _tool = tool;
        bar.Controls.Add(b);
    }

    private void AddColorSwatch(FlowLayoutPanel bar, Color c)
    {
        var b = new Button
        {
            BackColor = c,
            Width = 24,
            Height = 24,
            Margin = new Padding(2),
            FlatStyle = FlatStyle.Flat
        };
        b.Click += (_, _) => SetColor(c);
        bar.Controls.Add(b);
    }

    private void AddThicknessButton(FlowLayoutPanel bar, string text, float value)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += (_, _) => _thickness = value;
        bar.Controls.Add(b);
    }

    private void AddActionButton(FlowLayoutPanel bar, string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += onClick;
        bar.Controls.Add(b);
    }

    private void SetColor(Color c)
    {
        _color = c;
        _btnColor.BackColor = c;
    }

    private void BuildCanvas()
    {
        _canvas.Dock = DockStyle.Fill;
        _canvas.BackColor = Color.DimGray;
        // Centraliza a imagem sem esticá-la; o desenho usa coordenadas da imagem.
        _canvas.SizeMode = PictureBoxSizeMode.CenterImage;
        _canvas.Image = _capture;
        _canvas.MouseDown += Canvas_MouseDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseUp += Canvas_MouseUp;
        _canvas.Paint += Canvas_Paint;
    }

    private void BuildAiPanel()
    {
        _aiPanel.Dock = DockStyle.Bottom;
        _aiPanel.Height = 120;
        _aiPanel.Visible = false; // só aparece após perguntar à IA
        _aiPanel.Padding = new Padding(4);

        _aiOutput.Dock = DockStyle.Fill;
        _aiOutput.Multiline = true;
        _aiOutput.ReadOnly = true;
        _aiOutput.ScrollBars = ScrollBars.Vertical;
        _aiOutput.WordWrap = true;

        var header = new Label { Text = "Resposta da IA:", Dock = DockStyle.Top, AutoSize = true };
        _aiPanel.Controls.Add(_aiOutput);
        _aiPanel.Controls.Add(header);
    }

    // ----- Conversão de coordenadas tela -> imagem -----

    /// <summary>
    /// Converte um ponto do PictureBox (CenterImage) para coordenadas da imagem base.
    /// </summary>
    private Point ToImagePoint(Point p)
    {
        int offsetX = (_canvas.ClientSize.Width - _capture.Width) / 2;
        int offsetY = (_canvas.ClientSize.Height - _capture.Height) / 2;
        return new Point(p.X - offsetX, p.Y - offsetY);
    }

    // ----- Interação do mouse -----

    private void Canvas_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var p = ToImagePoint(e.Location);

        if (_tool == ToolType.Text)
        {
            // Texto não usa arraste: pede a string e cria o shape direto.
            var text = PromptForText();
            if (!string.IsNullOrEmpty(text))
            {
                _shapes.Add(new TextShape { Location = p, Text = text, Color = _color, Thickness = _thickness });
                _canvas.Invalidate();
            }
            return;
        }

        _current = _tool switch
        {
            ToolType.Arrow => new ArrowShape { Start = p, End = p, Color = _color, Thickness = _thickness },
            ToolType.Rectangle => new RectShape { Start = p, End = p, Color = _color, Thickness = _thickness },
            ToolType.Ellipse => new EllipseShape { Start = p, End = p, Color = _color, Thickness = _thickness },
            ToolType.Line => new LineShape { Start = p, End = p, Color = _color, Thickness = _thickness },
            ToolType.Pen => CreatePen(p),
            _ => null
        };
    }

    private PenShape CreatePen(Point p)
    {
        var pen = new PenShape { Color = _color, Thickness = _thickness };
        pen.Points.Add(p);
        return pen;
    }

    private void Canvas_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_current is null) return;
        var p = ToImagePoint(e.Location);

        if (_current is PenShape pen)
            pen.Points.Add(p);
        else if (_current is TwoPointShape two)
            two.End = p;

        _canvas.Invalidate();
    }

    private void Canvas_MouseUp(object? sender, MouseEventArgs e)
    {
        if (_current is null) return;
        // Descarta cliques sem arraste (shapes degenerados), exceto o lápis.
        bool keep = _current is PenShape p ? p.Points.Count > 1
                  : _current is TwoPointShape t && t.Start != t.End;
        if (keep) _shapes.Add(_current);
        _current = null;
        _canvas.Invalidate();
    }

    private void Canvas_Paint(object? sender, PaintEventArgs e)
    {
        // O PictureBox já desenha a imagem base (CenterImage). Desenhamos as
        // anotações por cima, transladando para o sistema de coordenadas da imagem.
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        int offsetX = (_canvas.ClientSize.Width - _capture.Width) / 2;
        int offsetY = (_canvas.ClientSize.Height - _capture.Height) / 2;
        var saved = g.Save();
        g.TranslateTransform(offsetX, offsetY);

        foreach (var s in _shapes) s.Draw(g);
        _current?.Draw(g);

        g.Restore(saved);
    }

    // ----- Undo / teclado -----

    private void Undo()
    {
        if (_shapes.Count > 0)
        {
            _shapes.RemoveAt(_shapes.Count - 1);
            _canvas.Invalidate();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.Z)
        {
            Undo();
            e.Handled = true;
        }
    }

    // ----- Diálogo simples de entrada de texto -----

    /// <summary>Mini-diálogo modal para digitar o texto da anotação.</summary>
    private string? PromptForText()
    {
        using var dlg = new Form
        {
            Text = "Texto da anotação",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(320, 100),
            MinimizeBox = false,
            MaximizeBox = false
        };
        var tb = new TextBox { Dock = DockStyle.Top, Margin = new Padding(8) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dlg.Controls.Add(tb);
        dlg.Controls.Add(ok);
        dlg.AcceptButton = ok;
        return dlg.ShowDialog(this) == DialogResult.OK ? tb.Text : null;
    }

    // ----- Rasterização final -----

    /// <summary>
    /// Rasteriza a imagem base + todas as anotações num novo Bitmap.
    /// Usado por Copiar/Salvar/Upload/IA. O chamador é dono do Bitmap retornado.
    /// </summary>
    private Bitmap RenderFinal()
    {
        var bmp = new Bitmap(_capture.Width, _capture.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.DrawImageUnscaled(_capture, 0, 0);
            foreach (var s in _shapes) s.Draw(g);
        }
        return bmp;
    }

    // ----- Ações da toolbar -----

    private void OnCopy(object? sender, EventArgs e)
    {
        try
        {
            using var final = RenderFinal();
            _services.CopyToClipboard(final);
        }
        catch (Exception ex)
        {
            ShowError("Falha ao copiar", ex);
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        try
        {
            using var final = RenderFinal();
            _services.SaveToFile(final);
        }
        catch (Exception ex)
        {
            ShowError("Falha ao salvar", ex);
        }
    }

    private async Task OnUploadAsync()
    {
        var original = _btnUpload.Text;
        _btnUpload.Enabled = false;
        _btnUpload.Text = "Enviando...";
        try
        {
            using var final = RenderFinal();
            var url = await _services.UploadAsync(final);
            // Mostra a URL e copia para a área de transferência.
            try { Clipboard.SetText(url); } catch { /* clipboard pode falhar; ignora */ }
            MessageBox.Show(this, $"Upload concluído:\n{url}\n\n(URL copiada para a área de transferência)",
                "Upload", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError("Falha no upload", ex);
        }
        finally
        {
            _btnUpload.Enabled = true;
            _btnUpload.Text = original;
        }
    }

    private async Task OnAskAiAsync()
    {
        var question = PromptForText(); // reaproveita o mini-diálogo como caixa de pergunta
        if (string.IsNullOrWhiteSpace(question)) return;

        var original = _btnAsk.Text;
        _btnAsk.Enabled = false;
        _btnAsk.Text = "Perguntando...";
        _aiPanel.Visible = true;
        _aiOutput.Text = "Aguardando resposta da IA...";
        try
        {
            using var final = RenderFinal();
            var answer = await _services.AskAiAsync(question, final);
            _aiOutput.Text = answer;
        }
        catch (Exception ex)
        {
            _aiOutput.Text = $"Erro: {ex.Message}";
            ShowError("Falha ao perguntar à IA", ex);
        }
        finally
        {
            _btnAsk.Enabled = true;
            _btnAsk.Text = original;
        }
    }

    private void ShowError(string title, Exception ex) =>
        MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
