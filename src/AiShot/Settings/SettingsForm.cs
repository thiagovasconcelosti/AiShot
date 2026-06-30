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

    public SettingsForm(AppConfig cfg)
    {
        _cfg = cfg;
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
        Row("Tecla", _hotKey);

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
