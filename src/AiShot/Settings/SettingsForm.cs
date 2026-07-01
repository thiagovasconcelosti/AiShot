using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using AiShot.Config;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AiShot.Settings;

/// <summary>
/// Janela de configuração — UI React + shadcn/ui hospedada em WebView2.
/// A ponte C#↔JS troca a config e a captura de atalho por mensagens.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppConfig _cfg;
    private readonly AiShot.HotKey.GlobalHotKey? _hotKeyService;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };

    public SettingsForm(AppConfig cfg, AiShot.HotKey.GlobalHotKey? hotKeyService = null)
    {
        _cfg = cfg;
        _hotKeyService = hotKeyService;

        Text = "AiShot — Configurações";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 680);
        BackColor = Color.FromArgb(11, 11, 13);
        Icon = LoadFormIcon();

        Controls.Add(_web);

        FormClosed += (_, _) =>
        {
            if (_hotKeyService is not null)
            {
                _hotKeyService.CaptureMode = false;
                _hotKeyService.KeyCaptured -= OnHotKeyCaptured;
            }
        };
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try { await InitWebAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show("Falha ao carregar a interface: " + ex.Message, "AiShot",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task InitWebAsync()
    {
        var uiDir = ExtractWebUI();
        var env = await CoreWebView2Environment.CreateAsync(
            userDataFolder: Path.Combine(Path.GetTempPath(), "AiShot.WebView2"));
        await _web.EnsureCoreWebView2Async(env);

        var core = _web.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("aishot.local", uiDir, CoreWebView2HostResourceAccessKind.Allow);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.WebMessageReceived += OnWebMessage;

        if (_hotKeyService is not null)
            _hotKeyService.KeyCaptured += OnHotKeyCaptured;

        _web.Source = new Uri("https://aishot.local/index.html");
    }

    // ---------- Ponte JS -> C# ----------
    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(e.WebMessageAsJson); }
        catch { return; }
        using (doc)
        {
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "ready": SendConfig(); break;
                case "save": Save(root.GetProperty("config")); break;
                case "cancel": DialogResult = DialogResult.Cancel; Close(); break;
                case "hotkeyStart": if (_hotKeyService is not null) _hotKeyService.CaptureMode = true; break;
                case "hotkeyStop": if (_hotKeyService is not null) _hotKeyService.CaptureMode = false; break;
                case "openUrl": OpenUrl(root.TryGetProperty("url", out var u) ? u.GetString() : null); break;
            }
        }
    }

    private void SendConfig()
    {
        var ai = _cfg.Ai;
        ai.Fallback ??= new AiEndpoint();
        var payload = new
        {
            type = "config",
            config = new
            {
                hotKey = _cfg.HotKey,
                closeOnCopy = _cfg.CloseOnCopy,
                ai = new
                {
                    provider = ai.Provider,
                    apiKey = ai.ApiKey,
                    model = ai.Model,
                    baseUrl = ai.BaseUrl,
                    fallback = new { provider = ai.Fallback.Provider, apiKey = ai.Fallback.ApiKey, model = ai.Fallback.Model, baseUrl = ai.Fallback.BaseUrl },
                    vision = new { enabled = ai.Vision.Enabled, provider = ai.Vision.Provider, apiKey = ai.Vision.ApiKey, model = ai.Vision.Model, baseUrl = ai.Vision.BaseUrl },
                },
                imageUpload = new { service = _cfg.ImageUpload.Service, apiKey = _cfg.ImageUpload.ApiKey },
            },
        };
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
    }

    private void Save(JsonElement c)
    {
        static string S(JsonElement e, string p) => e.TryGetProperty(p, out var v) ? (v.GetString() ?? "") : "";
        static bool B(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;

        _cfg.HotKey = S(c, "hotKey").Trim();
        _cfg.CloseOnCopy = B(c, "closeOnCopy");

        var ai = c.GetProperty("ai");
        _cfg.Ai.Provider = S(ai, "provider");
        _cfg.Ai.ApiKey = S(ai, "apiKey").Trim();
        _cfg.Ai.Model = S(ai, "model").Trim();
        _cfg.Ai.BaseUrl = S(ai, "baseUrl").Trim();

        _cfg.Ai.Fallback ??= new AiEndpoint();
        var fb = ai.GetProperty("fallback");
        _cfg.Ai.Fallback.Provider = S(fb, "provider");
        _cfg.Ai.Fallback.ApiKey = S(fb, "apiKey").Trim();
        _cfg.Ai.Fallback.Model = S(fb, "model").Trim();
        _cfg.Ai.Fallback.BaseUrl = S(fb, "baseUrl").Trim();

        var vis = ai.GetProperty("vision");
        _cfg.Ai.Vision.Enabled = B(vis, "enabled");
        _cfg.Ai.Vision.Provider = S(vis, "provider");
        _cfg.Ai.Vision.ApiKey = S(vis, "apiKey").Trim();
        _cfg.Ai.Vision.Model = S(vis, "model").Trim();
        _cfg.Ai.Vision.BaseUrl = S(vis, "baseUrl").Trim();

        var up = c.GetProperty("imageUpload");
        _cfg.ImageUpload.Service = S(up, "service");
        _cfg.ImageUpload.ApiKey = S(up, "apiKey").Trim();

        _cfg.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnHotKeyCaptured(Keys vk)
    {
        if (IsDisposed || _web.CoreWebView2 is null) return;
        if (InvokeRequired) { BeginInvoke(new Action<Keys>(OnHotKeyCaptured), vk); return; }
        var combo = ComboString(Control.ModifierKeys, vk);
        var msg = JsonSerializer.Serialize(new { type = "hotkeyCaptured", combo });
        _web.CoreWebView2.PostWebMessageAsJson(msg);
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

    private static void OpenUrl(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = u.AbsoluteUri, UseShellExecute = true }); }
        catch { }
    }

    // ---------- Recursos ----------
    /// <summary>Extrai a UI web embutida para uma pasta temporária.</summary>
    private static string ExtractWebUI()
    {
        var version = typeof(SettingsForm).Assembly.GetName().Version?.ToString() ?? "0";
        var dir = Path.Combine(Path.GetTempPath(), "AiShot.webui", version);
        var asm = Assembly.GetExecutingAssembly();

        foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith("webui/", StringComparison.Ordinal)))
        {
            var rel = res["webui/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var target = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var s = asm.GetManifestResourceStream(res)!;
            using var f = File.Create(target);
            s.CopyTo(f);
        }
        return dir;
    }

    private static Icon? LoadFormIcon()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
        if (name is null) return null;
        using var s = asm.GetManifestResourceStream(name);
        return s is null ? null : new Icon(s);
    }
}
