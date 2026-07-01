using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;
using AiShot.Config;

namespace AiShot.Settings;

/// <summary>
/// Janela de configuração — design dark coeso com o app (cards arredondados,
/// inputs flat, header com ícone, rodapé discreto). Salva em appsettings.json.
/// </summary>
public sealed class SettingsForm : Form
{
    // Paleta (shadcn dark)
    private static readonly Color Bg = Color.FromArgb(11, 11, 13);
    private static readonly Color Card = Color.FromArgb(24, 24, 27);
    private static readonly Color CardBorder = Color.FromArgb(39, 39, 42);
    private static readonly Color InputBg = Color.FromArgb(32, 32, 36);
    private static readonly Color InputBorder = Color.FromArgb(63, 63, 70);
    private static readonly Color TextCol = Color.FromArgb(244, 244, 245);
    private static readonly Color Muted = Color.FromArgb(161, 161, 170);
    private static readonly Color Accent = Color.FromArgb(47, 107, 255);
    private static readonly Color AccentHover = Color.FromArgb(74, 128, 255);

    private const string RepoUrl = "https://github.com/thiagovasconcelosti/AiShot";
    private const string DocsUrl = "https://thiagovasconcelosti.github.io/AiShot/";

    private readonly AppConfig _cfg;
    private readonly AiShot.HotKey.GlobalHotKey? _hotKeyService;

    private readonly TextBox _hotKey = new();
    private readonly ComboBox _provider = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _apiKey = new() { UseSystemPasswordChar = true };
    private readonly TextBox _model = new();
    private readonly TextBox _baseUrl = new();

    private readonly ComboBox _fbProvider = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _fbApiKey = new() { UseSystemPasswordChar = true };
    private readonly TextBox _fbModel = new();

    private readonly CheckBox _visEnabled = new() { Text = "Ativar IA de visão (lê a imagem antes da IA principal)" };
    private readonly ComboBox _visProvider = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _visApiKey = new() { UseSystemPasswordChar = true };
    private readonly TextBox _visModel = new();

    private readonly ComboBox _imgService = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _imgApiKey = new() { UseSystemPasswordChar = true };

    private readonly CheckBox _closeOnCopy = new() { Text = "Fechar ao copiar", AutoSize = true };

    private FlowLayoutPanel _stack = null!;

    public SettingsForm(AppConfig cfg, AiShot.HotKey.GlobalHotKey? hotKeyService = null)
    {
        _cfg = cfg;
        _hotKeyService = hotKeyService;

        Text = "AiShot — Configurações";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 660);
        BackColor = Bg;
        ForeColor = TextCol;
        Font = new Font("Segoe UI", 9f);

        foreach (var c in new[] { _provider, _fbProvider, _visProvider })
            c.Items.AddRange(new object[] { "anthropic", "openai" });
        _imgService.Items.AddRange(new object[] { "freeimage", "imgbb" });

        BuildHeader();
        BuildFooter();   // rodapé + botões (Dock Bottom) antes do conteúdo (Fill)
        BuildContent();

        LoadValues();
    }

    // ---------- Layout ----------
    private void BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Bg };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(CardBorder, 1);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var icon = new PictureBox
        {
            Image = LoadIcon(28),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(28, 28),
            Location = new Point(18, 15),
            BackColor = Color.Transparent,
        };
        var title = new Label
        {
            Text = "Configurações",
            AutoSize = true,
            ForeColor = TextCol,
            Font = new Font("Segoe UI Semibold", 12f),
            Location = new Point(56, 12),
        };
        var subtitle = new Label
        {
            Text = "Atalho, provedores de IA e upload",
            AutoSize = true,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 8.25f),
            Location = new Point(58, 34),
        };
        header.Controls.Add(icon);
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        Controls.Add(header);
    }

    private void BuildContent()
    {
        _stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Bg,
            Padding = new Padding(16, 14, 16, 14),
        };

        // Atalho
        var (atalho, gAtalho) = NewCard("Atalho", "Tecla global para capturar");
        AddRow(gAtalho, "Tecla", BuildHotKeyRow());

        // IA principal
        var (main, gMain) = NewCard("IA principal", "Modelo usado para responder");
        AddRow(gMain, "Provider", Style(_provider));
        AddRow(gMain, "API Key", Password(_apiKey));
        AddRow(gMain, "Modelo", Style(_model));
        AddRow(gMain, "Base URL", Style(_baseUrl), "opcional");

        // Fallback
        var (fb, gFb) = NewCard("IA de fallback", "Usada se a principal falhar");
        AddRow(gFb, "Provider", Style(_fbProvider));
        AddRow(gFb, "API Key", Password(_fbApiKey));
        AddRow(gFb, "Modelo", Style(_fbModel));

        // Visão
        var (vis, gVis) = NewCard("IA de visão", "Descreve a imagem antes da IA principal");
        AddSpanning(gVis, Style(_visEnabled));
        AddRow(gVis, "Provider", Style(_visProvider));
        AddRow(gVis, "API Key", Password(_visApiKey));
        AddRow(gVis, "Modelo", Style(_visModel));

        // Upload
        var (up, gUp) = NewCard("Upload de imagem", "Serviço de hospedagem do print");
        AddRow(gUp, "Serviço", Style(_imgService));
        AddRow(gUp, "API Key", Password(_imgApiKey), "opcional");

        // Comportamento
        var (beh, gBeh) = NewCard("Comportamento", null);
        AddSpanning(gBeh, Style(_closeOnCopy));

        foreach (var card in new[] { atalho, main, fb, vis, up, beh })
            _stack.Controls.Add(card);

        Controls.Add(_stack);
        _stack.BringToFront();

        // largura dos cards acompanha o painel
        _stack.ClientSizeChanged += (_, _) => ResizeCards();
        ResizeCards();
    }

    private void ResizeCards()
    {
        int w = _stack.ClientSize.Width - _stack.Padding.Horizontal;
        foreach (Control card in _stack.Controls)
            card.Width = w;
    }

    private void BuildFooter()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Bg };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(CardBorder, 1);
            e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0);
        };

        var btnSave = MakeButton("Salvar", accent: true);
        btnSave.DialogResult = DialogResult.OK;
        btnSave.Location = new Point(0, 12); // reposicionado no Resize
        btnSave.Click += (_, _) => Persist();

        var btnCancel = MakeButton("Cancelar", accent: false);
        btnCancel.DialogResult = DialogResult.Cancel;

        var links = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Location = new Point(18, 18),
            BackColor = Color.Transparent,
        };
        links.Controls.Add(MakeFooterLink("Repositório", RepoUrl));
        links.Controls.Add(new Label { Text = "·", AutoSize = true, ForeColor = Muted, Margin = new Padding(6, 3, 6, 0) });
        links.Controls.Add(MakeFooterLink("Documentação", DocsUrl));

        void Reposition()
        {
            btnSave.Location = new Point(bar.Width - btnSave.Width - 16, 12);
            btnCancel.Location = new Point(btnSave.Left - btnCancel.Width - 8, 12);
        }
        bar.Resize += (_, _) => Reposition();

        bar.Controls.Add(links);
        bar.Controls.Add(btnCancel);
        bar.Controls.Add(btnSave);
        Controls.Add(bar);
        Reposition();

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    // ---------- Componentes ----------
    private (RoundPanel card, TableLayoutPanel body) NewCard(string title, string? subtitle)
    {
        var card = new RoundPanel
        {
            FillColor = Card,
            BorderColor = CardBorder,
            Radius = 10,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(16, 12, 16, 14),
            BackColor = Bg,
        };

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = Color.Transparent,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var head = new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = TextCol,
            Font = new Font("Segoe UI Semibold", 10f),
            Margin = new Padding(0, 0, 0, subtitle is null ? 8 : 1),
        };
        body.Controls.Add(head);
        body.SetColumnSpan(head, 2);

        if (subtitle is not null)
        {
            var sub = new Label
            {
                Text = subtitle,
                AutoSize = true,
                ForeColor = Muted,
                Font = new Font("Segoe UI", 8.25f),
                Margin = new Padding(0, 0, 0, 10),
            };
            body.Controls.Add(sub);
            body.SetColumnSpan(sub, 2);
        }

        card.Controls.Add(body);
        return (card, body);
    }

    private void AddRow(TableLayoutPanel body, string label, Control field, string? hint = null)
    {
        var lbl = new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = Muted,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 8, 6),
        };
        field.Margin = new Padding(0, 4, 0, 4);
        field.Dock = DockStyle.Fill;
        body.Controls.Add(lbl);
        body.Controls.Add(field);

        if (hint is not null)
        {
            // sufixo discreto ao lado do label
            lbl.Text = label;
            var h = new Label { Text = hint, AutoSize = true, ForeColor = Color.FromArgb(110, 110, 120), Font = new Font("Segoe UI", 7.5f), Anchor = AnchorStyles.Left, Margin = new Padding(0, 11, 0, 0) };
            body.Controls.Add(new Label { Width = 0, Height = 0, Margin = Padding.Empty }); // filler col0
            body.Controls.Add(h);
        }
    }

    private void AddSpanning(TableLayoutPanel body, Control field)
    {
        field.Margin = new Padding(0, 6, 0, 4);
        body.Controls.Add(field);
        body.SetColumnSpan(field, 2);
    }

    /// <summary>Estiliza um TextBox dentro de um painel arredondado (input flat).</summary>
    private Control Style(TextBox tb)
    {
        tb.BorderStyle = BorderStyle.None;
        tb.BackColor = InputBg;
        tb.ForeColor = TextCol;
        tb.Font = new Font("Segoe UI", 9.5f);

        var wrap = new RoundPanel
        {
            FillColor = InputBg,
            BorderColor = InputBorder,
            Radius = 7,
            Height = 30,
            BackColor = Card,
            Padding = new Padding(9, 6, 9, 6),
        };
        tb.Dock = DockStyle.Fill;
        wrap.Controls.Add(tb);
        wrap.Tag = tb; // permite recuperar o textbox
        return wrap;
    }

    private Control Password(TextBox tb)
    {
        tb.BorderStyle = BorderStyle.None;
        tb.BackColor = InputBg;
        tb.ForeColor = TextCol;
        tb.UseSystemPasswordChar = true;
        tb.Font = new Font("Segoe UI", 9.5f);

        var wrap = new RoundPanel
        {
            FillColor = InputBg,
            BorderColor = InputBorder,
            Radius = 7,
            Height = 30,
            BackColor = Card,
            Padding = new Padding(9, 6, 4, 6),
        };
        var eye = new Label
        {
            Text = "👁",
            AutoSize = false,
            Width = 26,
            Dock = DockStyle.Right,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Muted,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Emoji", 9f),
        };
        eye.Click += (_, _) => { tb.UseSystemPasswordChar = !tb.UseSystemPasswordChar; eye.ForeColor = tb.UseSystemPasswordChar ? Muted : Accent; };
        tb.Dock = DockStyle.Fill;
        wrap.Controls.Add(tb);
        wrap.Controls.Add(eye);
        eye.BringToFront();
        return wrap;
    }

    private ComboBox Style(ComboBox cb)
    {
        cb.FlatStyle = FlatStyle.Flat;
        cb.BackColor = InputBg;
        cb.ForeColor = TextCol;
        cb.Font = new Font("Segoe UI", 9.5f);
        cb.Height = 28;
        return cb;
    }

    private CheckBox Style(CheckBox ck)
    {
        ck.ForeColor = TextCol;
        ck.Font = new Font("Segoe UI", 9f);
        ck.FlatStyle = FlatStyle.Flat;
        ck.FlatAppearance.BorderColor = InputBorder;
        return ck;
    }

    private Button MakeButton(string text, bool accent)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(accent ? 104 : 100, 32),
            FlatStyle = FlatStyle.Flat,
            ForeColor = accent ? Color.White : TextCol,
            BackColor = accent ? Accent : InputBg,
            Font = new Font("Segoe UI Semibold", 9f),
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = accent ? 0 : 1;
        b.FlatAppearance.BorderColor = InputBorder;
        b.FlatAppearance.MouseOverBackColor = accent ? AccentHover : CardBorder;
        b.Region = new Region(RoundRect(new Rectangle(0, 0, b.Width, b.Height), 8));
        return b;
    }

    private LinkLabel MakeFooterLink(string text, string url)
    {
        var l = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 8f),
            LinkColor = Muted,
            ActiveLinkColor = Accent,
            VisitedLinkColor = Muted,
            LinkBehavior = LinkBehavior.AlwaysUnderline,
            Margin = new Padding(0, 3, 0, 0),
        };
        l.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch { /* ignora */ }
        };
        return l;
    }

    // ---------- Campo de atalho ----------
    private Control BuildHotKeyRow()
    {
        _hotKey.ReadOnly = true;
        _hotKey.Cursor = Cursors.Hand;
        _hotKey.TabStop = true;
        _hotKey.BorderStyle = BorderStyle.None;
        _hotKey.BackColor = InputBg;
        _hotKey.ForeColor = TextCol;
        _hotKey.Font = new Font("Segoe UI", 9.5f);
        _hotKey.GotFocus += (_, _) => { if (_hotKeyService is not null) _hotKeyService.CaptureMode = true; };
        _hotKey.LostFocus += (_, _) => { if (_hotKeyService is not null) _hotKeyService.CaptureMode = false; };

        if (_hotKeyService is not null)
            _hotKeyService.KeyCaptured += OnHotKeyCaptured;
        else
            _hotKey.KeyDown += HotKey_KeyDown;

        FormClosed += (_, _) =>
        {
            if (_hotKeyService is not null)
            {
                _hotKeyService.CaptureMode = false;
                _hotKeyService.KeyCaptured -= OnHotKeyCaptured;
            }
        };

        var wrap = new RoundPanel
        {
            FillColor = InputBg,
            BorderColor = InputBorder,
            Radius = 7,
            Height = 30,
            BackColor = Card,
            Padding = new Padding(9, 6, 4, 6),
        };
        var clear = new Label
        {
            Text = "Limpar",
            AutoSize = false,
            Width = 52,
            Dock = DockStyle.Right,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Muted,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 8.25f),
        };
        clear.Click += (_, _) => { _hotKey.Text = ""; _hotKey.Focus(); };
        _hotKey.Dock = DockStyle.Fill;
        wrap.Controls.Add(_hotKey);
        wrap.Controls.Add(clear);
        clear.BringToFront();
        return wrap;
    }

    private void OnHotKeyCaptured(Keys vk)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action<Keys>(OnHotKeyCaptured), vk); return; }

        var mods = Control.ModifierKeys;
        var parts = new List<string>();
        if (mods.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(Keys.Shift)) parts.Add("Shift");
        parts.Add(vk.ToString());
        _hotKey.Text = string.Join("+", parts);
    }

    private static void HotKey_KeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        var box = (TextBox)sender!;
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;

        var parts = new List<string>();
        if (e.Control) parts.Add("Ctrl");
        if (e.Alt) parts.Add("Alt");
        if (e.Shift) parts.Add("Shift");
        parts.Add(e.KeyCode.ToString());
        box.Text = string.Join("+", parts);
    }

    // ---------- Dados ----------
    private void LoadValues()
    {
        _hotKey.Text = _cfg.HotKey;
        _closeOnCopy.Checked = _cfg.CloseOnCopy;
        _provider.SelectedItem = _cfg.Ai.Provider;
        _apiKey.Text = _cfg.Ai.ApiKey;
        _model.Text = _cfg.Ai.Model;
        _baseUrl.Text = _cfg.Ai.BaseUrl;

        _cfg.Ai.Fallback ??= new AiEndpoint();
        _fbProvider.SelectedItem = _cfg.Ai.Fallback.Provider;
        _fbApiKey.Text = _cfg.Ai.Fallback.ApiKey;
        _fbModel.Text = _cfg.Ai.Fallback.Model;

        _visEnabled.Checked = _cfg.Ai.Vision.Enabled;
        _visProvider.SelectedItem = _cfg.Ai.Vision.Provider;
        _visApiKey.Text = _cfg.Ai.Vision.ApiKey;
        _visModel.Text = _cfg.Ai.Vision.Model;

        _imgService.SelectedItem = _cfg.ImageUpload.Service;
        _imgApiKey.Text = _cfg.ImageUpload.ApiKey;
    }

    private void Persist()
    {
        _cfg.HotKey = _hotKey.Text.Trim();
        _cfg.CloseOnCopy = _closeOnCopy.Checked;
        _cfg.Ai.Provider = (string)(_provider.SelectedItem ?? "anthropic");
        _cfg.Ai.ApiKey = _apiKey.Text.Trim();
        _cfg.Ai.Model = _model.Text.Trim();
        _cfg.Ai.BaseUrl = _baseUrl.Text.Trim();

        _cfg.Ai.Fallback ??= new AiEndpoint();
        _cfg.Ai.Fallback.Provider = (string)(_fbProvider.SelectedItem ?? "openai");
        _cfg.Ai.Fallback.ApiKey = _fbApiKey.Text.Trim();
        _cfg.Ai.Fallback.Model = _fbModel.Text.Trim();

        _cfg.Ai.Vision.Enabled = _visEnabled.Checked;
        _cfg.Ai.Vision.Provider = (string)(_visProvider.SelectedItem ?? "anthropic");
        _cfg.Ai.Vision.ApiKey = _visApiKey.Text.Trim();
        _cfg.Ai.Vision.Model = _visModel.Text.Trim();

        _cfg.ImageUpload.Service = (string)(_imgService.SelectedItem ?? "freeimage");
        _cfg.ImageUpload.ApiKey = _imgApiKey.Text.Trim();

        _cfg.Save();
    }

    // ---------- Utilidades ----------
    private static Image? LoadIcon(int size)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
        if (name is null) return null;
        using var s = asm.GetManifestResourceStream(name);
        if (s is null) return null;
        using var ico = new Icon(s, size, size);
        return ico.ToBitmap();
    }

    private static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Painel com cantos arredondados (fundo + borda), pintado sobre o pai.</summary>
    private sealed class RoundPanel : Panel
    {
        public Color FillColor = Color.Black;
        public Color BorderColor = Color.Gray;
        public int Radius = 8;

        public RoundPanel() { DoubleBuffered = true; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundRect(r, Radius);
            using var fill = new SolidBrush(FillColor);
            using var pen = new Pen(BorderColor, 1);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(pen, path);
            base.OnPaint(e);
        }
    }
}
