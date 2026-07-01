using System.Reflection;
using System.Windows.Forms;
using AiShot.Config;
using Sunny.UI;

namespace AiShot.Settings;

/// <summary>
/// Janela de configuração — controles nativos do SunnyUI (dark, arredondados,
/// dropdown/scroll temáticos). Sem custom-paint. Salva em appsettings.json.
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

    private readonly UITextBox _hotKey = new();
    private readonly UIComboBox _provider = new();
    private readonly UITextBox _apiKey = new();
    private readonly UITextBox _model = new();
    private readonly UITextBox _baseUrl = new();

    private readonly UIComboBox _fbProvider = new();
    private readonly UITextBox _fbApiKey = new();
    private readonly UITextBox _fbModel = new();

    private readonly UISwitch _visEnabled = new();
    private readonly UIComboBox _visProvider = new();
    private readonly UITextBox _visApiKey = new();
    private readonly UITextBox _visModel = new();

    private readonly UIComboBox _imgService = new();
    private readonly UITextBox _imgApiKey = new();

    private readonly UISwitch _closeOnCopy = new();

    public SettingsForm(AppConfig cfg, AiShot.HotKey.GlobalHotKey? hotKeyService = null)
    {
        _cfg = cfg;
        _hotKeyService = hotKeyService;

        Text = "AiShot — Configurações";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 668);
        BackColor = Bg;
        ForeColor = TextCol;
        Font = new Font("Segoe UI", 9f);

        foreach (var c in new[] { _provider, _fbProvider, _visProvider })
            c.Items.AddRange(new object[] { "anthropic", "openai" });
        _imgService.Items.AddRange(new object[] { "freeimage", "imgbb" });

        BuildHeader();
        BuildFooter();
        BuildContent();

        LoadValues();
    }

    // ---------- Layout ----------
    private void BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Bg };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(CardBorder, 1);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        header.Controls.Add(new PictureBox
        {
            Image = LoadIcon(30),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(30, 30),
            Location = new Point(18, 15),
            BackColor = Color.Transparent,
        });
        header.Controls.Add(new Label
        {
            Text = "Configurações",
            AutoSize = true,
            ForeColor = TextCol,
            Font = new Font("Segoe UI Semibold", 12.5f),
            Location = new Point(58, 12),
        });
        header.Controls.Add(new Label
        {
            Text = "Atalho, provedores de IA e upload",
            AutoSize = true,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 8.25f),
            Location = new Point(60, 35),
        });
        Controls.Add(header);
    }

    private void BuildContent()
    {
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Bg,
            Padding = new Padding(16, 14, 16, 14),
        };
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Bg,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var (atalho, gAtalho) = NewCard("Atalho", "Tecla global para capturar");
        AddRow(gAtalho, "Tecla", HotKeyRow());

        var (main, gMain) = NewCard("IA principal", "Modelo usado para responder");
        AddRow(gMain, "Provider", Combo(_provider));
        AddRow(gMain, "API Key", Password(_apiKey));
        AddRow(gMain, "Modelo", Input(_model));
        AddRow(gMain, "Base URL", Input(_baseUrl, "opcional"));

        var (fb, gFb) = NewCard("IA de fallback", "Usada se a principal falhar");
        AddRow(gFb, "Provider", Combo(_fbProvider));
        AddRow(gFb, "API Key", Password(_fbApiKey));
        AddRow(gFb, "Modelo", Input(_fbModel));

        var (vis, gVis) = NewCard("IA de visão", "Descreve a imagem antes da IA principal");
        AddSpanning(gVis, SwitchRow("Ativar IA de visão", _visEnabled));
        AddRow(gVis, "Provider", Combo(_visProvider));
        AddRow(gVis, "API Key", Password(_visApiKey));
        AddRow(gVis, "Modelo", Input(_visModel));

        var (up, gUp) = NewCard("Upload de imagem", "Serviço de hospedagem do print");
        AddRow(gUp, "Serviço", Combo(_imgService));
        AddRow(gUp, "API Key", Password(_imgApiKey, "opcional"));

        var (beh, gBeh) = NewCard("Comportamento", null);
        AddSpanning(gBeh, SwitchRow("Fechar ao copiar", _closeOnCopy));

        foreach (var card in new[] { atalho, main, fb, vis, up, beh })
        {
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 0, 12);
            stack.Controls.Add(card);
        }

        scroll.Controls.Add(stack);
        Controls.Add(scroll);
        scroll.BringToFront();
    }

    private void BuildFooter()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Bg };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(CardBorder, 1);
            e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0);
        };

        var btnSave = Button("Salvar", accent: true);
        btnSave.DialogResult = DialogResult.OK;
        btnSave.Click += (_, _) => Persist();

        var btnCancel = Button("Cancelar", accent: false);
        btnCancel.DialogResult = DialogResult.Cancel;

        var links = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Location = new Point(16, 18),
            BackColor = Color.Transparent,
        };
        links.Controls.Add(FooterLink("Repositório", RepoUrl));
        links.Controls.Add(new Label { Text = "·", AutoSize = true, ForeColor = Muted, Margin = new Padding(6, 3, 6, 0) });
        links.Controls.Add(FooterLink("Documentação", DocsUrl));

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

    // ---------- Componentes (SunnyUI) ----------
    private (UIPanel card, TableLayoutPanel body) NewCard(string title, string? subtitle)
    {
        var card = new UIPanel
        {
            StyleCustomMode = true,
            FillColor = Card,
            RectColor = CardBorder,
            Radius = 12,
            RadiusSides = UICornerRadiusSides.All,
            ForeColor = TextCol,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 12, 16, 14),
        };

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = Color.Transparent,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var head = new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = TextCol,
            Font = new Font("Segoe UI Semibold", 10.5f),
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

    private void AddRow(TableLayoutPanel body, string label, Control field)
    {
        var lbl = new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = Muted,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 9, 8, 6),
        };
        field.Margin = new Padding(0, 4, 0, 4);
        field.Dock = DockStyle.Fill;
        body.Controls.Add(lbl);
        body.Controls.Add(field);
    }

    private void AddSpanning(TableLayoutPanel body, Control field)
    {
        field.Margin = new Padding(0, 6, 0, 4);
        body.Controls.Add(field);
        body.SetColumnSpan(field, 2);
    }

    private UITextBox Input(UITextBox tb, string? watermark = null)
    {
        tb.StyleCustomMode = true;
        tb.FillColor = InputBg;
        tb.RectColor = InputBorder;
        tb.ForeColor = TextCol;
        tb.Radius = 8;
        tb.Font = new Font("Segoe UI", 9.5f);
        tb.Height = 30;
        if (watermark is not null) { tb.Watermark = watermark; tb.WatermarkColor = Color.FromArgb(110, 110, 120); }
        return tb;
    }

    private UITextBox Password(UITextBox tb, string? watermark = null)
    {
        Input(tb, watermark);
        tb.PasswordChar = '●';
        tb.ShowButton = true;
        tb.ButtonSymbol = 61550;          // FontAwesome eye
        tb.ButtonFillColor = InputBg;
        tb.ButtonForeColor = Muted;
        tb.ButtonFillHoverColor = InputBg;
        tb.ButtonForeHoverColor = Accent;
        tb.ButtonRectColor = InputBg;      // sem moldura em caixa
        tb.ButtonRectHoverColor = InputBg;
        tb.ButtonRectPressColor = InputBg;
        tb.ButtonClick += (_, _) =>
            tb.PasswordChar = tb.PasswordChar == '\0' ? '●' : '\0';
        return tb;
    }

    private UIComboBox Combo(UIComboBox cb)
    {
        cb.StyleCustomMode = true;
        cb.DropDownStyle = UIDropDownStyle.DropDownList;
        cb.FillColor = InputBg;
        cb.RectColor = InputBorder;
        cb.ForeColor = TextCol;
        cb.ItemFillColor = Card;
        cb.ItemForeColor = TextCol;
        cb.ItemSelectForeColor = Color.White;
        cb.ItemRectColor = Accent;
        cb.Radius = 8;
        cb.Font = new Font("Segoe UI", 9.5f);
        cb.Height = 30;
        return cb;
    }

    private Control SwitchRow(string text, UISwitch sw)
    {
        sw.StyleCustomMode = true;
        sw.ActiveColor = Accent;
        sw.InActiveColor = InputBorder;
        sw.ButtonColor = Color.White;
        sw.ActiveText = "";
        sw.InActiveText = "";
        sw.Size = new Size(42, 22);

        var row = new Panel { Height = 30, BackColor = Color.Transparent };
        var lbl = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = TextCol,
            Font = new Font("Segoe UI", 9.5f),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        sw.Anchor = AnchorStyles.Right;
        void Place() => sw.Location = new Point(row.Width - sw.Width - 2, (row.Height - sw.Height) / 2);
        row.Resize += (_, _) => Place();
        row.Controls.Add(sw);
        row.Controls.Add(lbl);
        Place();
        return row;
    }

    private UIButton Button(string text, bool accent)
    {
        var b = new UIButton
        {
            Text = text,
            StyleCustomMode = true,
            Radius = 8,
            Size = new Size(accent ? 104 : 100, 32),
            Font = new Font("Segoe UI Semibold", 9f),
            FillColor = accent ? Accent : InputBg,
            FillHoverColor = accent ? AccentHover : CardBorder,
            FillPressColor = accent ? AccentHover : CardBorder,
            RectColor = accent ? Accent : InputBorder,
            RectHoverColor = accent ? AccentHover : InputBorder,
            RectPressColor = accent ? AccentHover : InputBorder,
            ForeColor = accent ? Color.White : TextCol,
            ForeHoverColor = accent ? Color.White : TextCol,
            ForePressColor = accent ? Color.White : TextCol,
        };
        return b;
    }

    private LinkLabel FooterLink(string text, string url)
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
            catch { }
        };
        return l;
    }

    // ---------- Campo de atalho ----------
    private Control HotKeyRow()
    {
        Input(_hotKey);
        _hotKey.ReadOnly = true;
        _hotKey.Cursor = Cursors.Hand;
        _hotKey.ShowButton = true;
        _hotKey.ButtonSymbol = 61453;     // FontAwesome times (limpar)
        _hotKey.ButtonSymbolSize = 16;
        _hotKey.ButtonFillColor = InputBg;
        _hotKey.ButtonForeColor = Muted;
        _hotKey.ButtonFillHoverColor = InputBg;
        _hotKey.ButtonForeHoverColor = Accent;
        _hotKey.ButtonRectColor = InputBg;
        _hotKey.ButtonRectHoverColor = InputBg;
        _hotKey.ButtonRectPressColor = InputBg;
        _hotKey.ButtonClick += (_, _) => { _hotKey.Text = ""; _hotKey.Focus(); };

        _hotKey.Enter += (_, _) => { if (_hotKeyService is not null) _hotKeyService.CaptureMode = true; };
        _hotKey.Leave += (_, _) => { if (_hotKeyService is not null) _hotKeyService.CaptureMode = false; };

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
        return _hotKey;
    }

    private void OnHotKeyCaptured(Keys vk)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action<Keys>(OnHotKeyCaptured), vk); return; }
        _hotKey.Text = ComboString(Control.ModifierKeys, vk);
    }

    private static void HotKey_KeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
        ((UITextBox)sender!).Text = ComboString(e.Modifiers, e.KeyCode);
    }

    private static string ComboString(Keys mods, Keys key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(Keys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    // ---------- Dados ----------
    private void LoadValues()
    {
        _hotKey.Text = _cfg.HotKey;
        _closeOnCopy.Active = _cfg.CloseOnCopy;
        _provider.SelectedItem = _cfg.Ai.Provider;
        _apiKey.Text = _cfg.Ai.ApiKey;
        _model.Text = _cfg.Ai.Model;
        _baseUrl.Text = _cfg.Ai.BaseUrl;

        _cfg.Ai.Fallback ??= new AiEndpoint();
        _fbProvider.SelectedItem = _cfg.Ai.Fallback.Provider;
        _fbApiKey.Text = _cfg.Ai.Fallback.ApiKey;
        _fbModel.Text = _cfg.Ai.Fallback.Model;

        _visEnabled.Active = _cfg.Ai.Vision.Enabled;
        _visProvider.SelectedItem = _cfg.Ai.Vision.Provider;
        _visApiKey.Text = _cfg.Ai.Vision.ApiKey;
        _visModel.Text = _cfg.Ai.Vision.Model;

        _imgService.SelectedItem = _cfg.ImageUpload.Service;
        _imgApiKey.Text = _cfg.ImageUpload.ApiKey;
    }

    private void Persist()
    {
        _cfg.HotKey = _hotKey.Text.Trim();
        _cfg.CloseOnCopy = _closeOnCopy.Active;
        _cfg.Ai.Provider = (_provider.SelectedItem as string) ?? "anthropic";
        _cfg.Ai.ApiKey = _apiKey.Text.Trim();
        _cfg.Ai.Model = _model.Text.Trim();
        _cfg.Ai.BaseUrl = _baseUrl.Text.Trim();

        _cfg.Ai.Fallback ??= new AiEndpoint();
        _cfg.Ai.Fallback.Provider = (_fbProvider.SelectedItem as string) ?? "openai";
        _cfg.Ai.Fallback.ApiKey = _fbApiKey.Text.Trim();
        _cfg.Ai.Fallback.Model = _fbModel.Text.Trim();

        _cfg.Ai.Vision.Enabled = _visEnabled.Active;
        _cfg.Ai.Vision.Provider = (_visProvider.SelectedItem as string) ?? "anthropic";
        _cfg.Ai.Vision.ApiKey = _visApiKey.Text.Trim();
        _cfg.Ai.Vision.Model = _visModel.Text.Trim();

        _cfg.ImageUpload.Service = (_imgService.SelectedItem as string) ?? "freeimage";
        _cfg.ImageUpload.ApiKey = _imgApiKey.Text.Trim();

        _cfg.Save();
    }

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
}
