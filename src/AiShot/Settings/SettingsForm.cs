using System.Windows.Forms;
using AiShot.Config;

namespace AiShot.Settings;

/// <summary>
/// Janela de configuração simples: credenciais de IA, provider, fallback,
/// visão e serviço de upload de imagem. Salva em appsettings.json.
/// </summary>
public sealed class SettingsForm : Form
{
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

    public SettingsForm(AppConfig cfg, AiShot.HotKey.GlobalHotKey? hotKeyService = null)
    {
        _cfg = cfg;
        _hotKeyService = hotKeyService;
        Text = "AiShot — Configurações";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 620);
        Font = new Font("Segoe UI", 9f);

        foreach (var c in new[] { _provider, _fbProvider, _visProvider })
        {
            c.Items.AddRange(new object[] { "anthropic", "openai" });
        }
        _imgService.Items.AddRange(new object[] { "freeimage", "imgbb" });

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoScroll = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void Row(string label, Control c)
        {
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 6) });
            c.Dock = DockStyle.Fill;
            c.Margin = new Padding(0, 3, 0, 3);
            layout.Controls.Add(c);
        }
        void Section(string title)
        {
            var l = new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(0, 12, 0, 4) };
            layout.Controls.Add(l);
            layout.SetColumnSpan(l, 2);
        }

        Section("Atalho");
        Row("Tecla", BuildHotKeyRow());

        Section("IA principal");
        Row("Provider", _provider);
        Row("API Key", _apiKey);
        Row("Modelo", _model);
        Row("Base URL (opc.)", _baseUrl);

        Section("IA de fallback");
        Row("Provider", _fbProvider);
        Row("API Key", _fbApiKey);
        Row("Modelo", _fbModel);

        Section("IA de visão (opcional)");
        layout.Controls.Add(_visEnabled);
        layout.SetColumnSpan(_visEnabled, 2);
        _visEnabled.Margin = new Padding(0, 4, 0, 4);
        Row("Provider", _visProvider);
        Row("API Key", _visApiKey);
        Row("Modelo", _visModel);

        Section("Upload de imagem");
        Row("Serviço", _imgService);
        Row("API Key (opc.)", _imgApiKey);

        var btnSave = new Button { Text = "Salvar", DialogResult = DialogResult.OK, Width = 100 };
        var btnCancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Width = 100 };
        btnSave.Click += (_, _) => Persist();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(btnCancel);
        buttons.Controls.Add(btnSave);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = btnSave;
        CancelButton = btnCancel;

        LoadValues();
    }

    /// <summary>Campo de atalho que captura a combinação pressionada + botão Limpar.</summary>
    private Control BuildHotKeyRow()
    {
        _hotKey.ReadOnly = true;
        _hotKey.Cursor = Cursors.Hand;
        _hotKey.TabStop = true;
        _hotKey.Dock = DockStyle.Fill;
        _hotKey.GotFocus += (_, _) =>
        {
            _hotKey.BackColor = Color.FromArgb(230, 240, 255);
            // Suspende o atalho global enquanto captura (PrintScreen não dispara print).
            if (_hotKeyService is not null) _hotKeyService.CaptureMode = true;
        };
        _hotKey.LostFocus += (_, _) =>
        {
            _hotKey.BackColor = SystemColors.Window;
            if (_hotKeyService is not null) _hotKeyService.CaptureMode = false;
        };

        if (_hotKeyService is not null)
            _hotKeyService.KeyCaptured += OnHotKeyCaptured; // via hook (suprime a tecla)
        else
            _hotKey.KeyDown += HotKey_KeyDown;             // fallback sem serviço

        // Garante restaurar o atalho ao fechar a janela.
        FormClosed += (_, _) =>
        {
            if (_hotKeyService is not null)
            {
                _hotKeyService.CaptureMode = false;
                _hotKeyService.KeyCaptured -= OnHotKeyCaptured;
            }
        };

        var clear = new Button { Text = "Limpar", Width = 70, Dock = DockStyle.Right };
        clear.Click += (_, _) => { _hotKey.Text = ""; _hotKey.Focus(); };

        var panel = new Panel { Height = 24 };
        panel.Controls.Add(_hotKey); // Fill primeiro
        panel.Controls.Add(clear);   // Right por cima
        clear.BringToFront();
        return panel;
    }

    /// <summary>Recebe a tecla capturada pelo hook global e monta a combinação.</summary>
    private void OnHotKeyCaptured(Keys vk)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action<Keys>(OnHotKeyCaptured), vk); return; }

        var mods = Control.ModifierKeys;
        var parts = new List<string>();
        if (mods.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(Keys.Shift)) parts.Add("Shift");
        parts.Add(vk.ToString()); // ex.: PrintScreen, F10, A
        _hotKey.Text = string.Join("+", parts);
    }

    private static void HotKey_KeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        var box = (TextBox)sender!;

        // Ignora quando só um modificador é pressionado (aguarda a tecla principal).
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return;

        var parts = new List<string>();
        if (e.Control) parts.Add("Ctrl");
        if (e.Alt) parts.Add("Alt");
        if (e.Shift) parts.Add("Shift");
        parts.Add(e.KeyCode.ToString()); // ex.: PrintScreen, F10, A
        box.Text = string.Join("+", parts);
    }

    private void LoadValues()
    {
        _hotKey.Text = _cfg.HotKey;
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
}
