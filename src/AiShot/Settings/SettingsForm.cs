using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;
using AiShot.Config;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AiShot.Settings;

/// <summary>
/// Janela de configuração — UI React + shadcn/ui hospedada em WebView2.
/// O WebView2 é criado ao abrir e destruído ao fechar (memória ociosa ~0).
/// Só o ambiente (barato, sem browser) fica em cache p/ acelerar a abertura.
/// Ponte C#↔JS por mensagens.
/// </summary>
public sealed class SettingsForm : Form
{
    private static readonly Color DarkBg = Color.FromArgb(11, 11, 13);
    // Perfil do navegador embarcado. Fica junto do cache da interface, em
    // %LOCALAPPDATA%, e não na pasta temporária compartilhada.
    private static string WvDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiShot", "WebView2");

    // Ambiente compartilhado (não spawna browser) — só encurta a criação.
    private static Task<CoreWebView2Environment>? _envTask;

    private readonly AppConfig _cfg;
    private readonly AiShot.HotKey.GlobalHotKey? _hotKeyService;
    private readonly HttpClient _http;
    // Ambos são adicionados a Controls no construtor: o Form descarta os filhos
    // no próprio Dispose. O _web ainda é descartado explicitamente em
    // OnFormClosed, para encerrar os processos do navegador assim que a janela
    // fecha, em vez de esperar o descarte do formulário.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA2213:Campos descartáveis devem ser descartados",
        Justification = "Controle filho; descartado por Controls e explicitamente em OnFormClosed.")]
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = DarkBg };

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA2213:Campos descartáveis devem ser descartados",
        Justification = "Controle filho; o Form descarta o que está em Controls.")]
    private readonly LoadingOverlay _loading = new() { Dock = DockStyle.Fill };

    public SettingsForm(AppConfig cfg, AiShot.HotKey.GlobalHotKey? hotKeyService = null, HttpClient? http = null)
    {
        _cfg = cfg;
        _hotKeyService = hotKeyService;
        _http = http ?? HttpClientFactory.Create();

        Text = "AiShot — Configurações";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;   // header do React é a barra
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 680);
        BackColor = DarkBg;
        Icon = LoadFormIcon();
        Controls.Add(_web);
        Controls.Add(_loading);
        _loading.BringToFront();
        _loading.Start();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hwnd, int msg, int wParam, int lParam);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int border = 0x002A2727;                                         // #27272A
        DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));      // DWMWA_BORDER_COLOR
        int round = 2;                                                   // DWMWCP_ROUND
        DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));       // DWMWA_WINDOW_CORNER_PREFERENCE
    }

    /// <summary>Inicia o arraste da janela (chamado pelo header do React).</summary>
    private void StartDrag()
    {
        ReleaseCapture();
        SendMessage(Handle, 0x00A1 /*WM_NCLBUTTONDOWN*/, 0x2 /*HTCAPTION*/, 0);
    }

    /// <summary>Pré-cria o ambiente WebView2 (barato) no startup — 1ª abertura rápida.</summary>
    public static void Prewarm()
    {
        try { _envTask ??= CoreWebView2Environment.CreateAsync(userDataFolder: WvDataDir); }
        catch { /* runtime ausente — tratado ao abrir */ }
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            _envTask ??= CoreWebView2Environment.CreateAsync(userDataFolder: WvDataDir);
            await _web.EnsureCoreWebView2Async(await _envTask);

            var core = _web.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("aishot.local", ExtractWebUI(), CoreWebView2HostResourceAccessKind.Allow);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.WebMessageReceived += OnWebMessage;
            core.DOMContentLoaded += (_, _) => HideLoading();

            if (_hotKeyService is not null) _hotKeyService.KeyCaptured += OnHotKeyCaptured;

            _web.Source = new Uri("https://aishot.local/index.html");
        }
        catch (Exception ex)
        {
            HideLoading();
            MessageBox.Show("Falha ao carregar a interface: " + ex.Message, "AiShot",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void HideLoading()
    {
        if (IsDisposed) return;
        _loading.Stop();
        _loading.Visible = false;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_hotKeyService is not null)
        {
            _hotKeyService.CaptureMode = false;
            _hotKeyService.KeyCaptured -= OnHotKeyCaptured;
        }
        _web.Dispose(); // encerra os processos do browser -> memória ociosa ~0
        base.OnFormClosed(e);
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
                case "ready": SendConfig(); _ = CheckUpdateAsync(); break;
                // A URL enviada pela página é ignorada de propósito — a origem do
                // download é a UpdateInfo guardada no C# (ver DoUpdateAsync).
                case "startUpdate": _ = DoUpdateAsync(); break;
                case "save":
                    try { Save(root.GetProperty("config")); }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Não foi possível salvar as configurações: " + ex.Message,
                            "AiShot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    break;
                case "cancel": DialogResult = DialogResult.Cancel; Close(); break;
                case "hotkeyStart": if (_hotKeyService is not null) _hotKeyService.CaptureMode = true; break;
                case "hotkeyStop": if (_hotKeyService is not null) _hotKeyService.CaptureMode = false; break;
                case "dragStart": StartDrag(); break;
                case "openUrl": OpenUrl(root.TryGetProperty("url", out var u) ? u.GetString() : null); break;
            }
        }
    }

    private void SendConfig()
    {
        if (_web.CoreWebView2 is null) return;
        var ai = _cfg.Ai;
        ai.Fallback ??= new AiEndpoint();
        var payload = new
        {
            type = "config",
            config = new
            {
                appVersion = AiShot.App.UpdateService.Current.ToString(),
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
                history = new
                {
                    enabled = _cfg.History.Enabled,
                    maxItems = _cfg.History.MaxItems,
                    maxSizeMb = _cfg.History.MaxSizeMb,
                },
            },
        };
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
    }

    /// <summary>
    /// Atualização disponível, guardada do lado do C#. A interface web recebe
    /// apenas a versão e a URL para exibição — o download usa este objeto, não
    /// o que volta da ponte, para que uma página comprometida não consiga
    /// apontar o instalador para outro lugar.
    /// </summary>
    private AiShot.App.UpdateInfo? _atualizacao;

    private async Task CheckUpdateAsync()
    {
        var info = await AiShot.App.UpdateService.CheckAsync(_http).ConfigureAwait(false);
        if (info is null || IsDisposed) return;
        _atualizacao = info;
        var msg = JsonSerializer.Serialize(new { type = "updateAvailable", version = info.Version, url = info.Url });
        void Post() { if (!IsDisposed && _web.CoreWebView2 is not null) _web.CoreWebView2.PostWebMessageAsJson(msg); }
        if (InvokeRequired) BeginInvoke(Post); else Post();
    }

    private async Task DoUpdateAsync()
    {
        var info = _atualizacao;
        if (info is null) return;
        try
        {
            await AiShot.App.UpdateService
                .DownloadAndRunAsync(_http, info.Url, info.ChecksumUrl)
                .ConfigureAwait(true);
            Application.Exit(); // fecha o app para o instalador atualizar
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                MessageBox.Show("Falha ao atualizar: " + ex.Message, "AiShot",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Save(JsonElement c)
    {
        static string S(JsonElement e, string p) => e.TryGetProperty(p, out var v) ? (v.GetString() ?? "") : "";
        static bool B(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;
        // Campo numérico do formulário: um valor ausente ou não-numérico mantém
        // o que já estava configurado, em vez de zerar o limite.
        static int I(JsonElement e, string p, int padrao) =>
            e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
                ? n : padrao;

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

        // O histórico é opcional na mensagem: uma página de versão anterior não
        // o envia, e sobrescrever com os padrões apagaria a escolha do usuário.
        if (c.TryGetProperty("history", out var hist))
        {
            _cfg.History.Enabled = B(hist, "enabled");
            _cfg.History.MaxItems = I(hist, "maxItems", _cfg.History.MaxItems);
            _cfg.History.MaxSizeMb = I(hist, "maxSizeMb", _cfg.History.MaxSizeMb);
        }

        _cfg.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnHotKeyCaptured(Keys vk)
    {
        if (IsDisposed || _web.CoreWebView2 is null) return;
        if (InvokeRequired) { BeginInvoke(new Action<Keys>(OnHotKeyCaptured), vk); return; }
        var msg = JsonSerializer.Serialize(new { type = "hotkeyCaptured", combo = ComboString(Control.ModifierKeys, vk) });
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

    /// <summary>
    /// Raiz dos caches do bundle web (um subdiretório por build).
    /// </summary>
    /// <remarks>
    /// Fica em %LOCALAPPDATA%, e não em %TEMP%: o WebView2 carrega HTML e
    /// JavaScript daqui, e a pasta temporária é um diretório de escrita comum a
    /// todo processo do usuário. O caminho por aplicativo reduz a chance de outro
    /// processo trocar o conteúdo entre a extração e a carga da página.
    /// </remarks>
    private static string WebUiCacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiShot", "webui");

    /// <summary>Raiz legada, em %TEMP% — limpa na inicialização.</summary>
    private static string LegacyWebUiCacheRoot => Path.Combine(Path.GetTempPath(), "AiShot.webui");

    /// <summary>
    /// Chave do cache: muda a cada build -> invalida o cache quando o bundle web
    /// muda; reutiliza entre aberturas do mesmo build.
    /// </summary>
    private static string WebUiCacheKey =>
        typeof(SettingsForm).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    /// <summary>
    /// Remove os caches de builds anteriores, na raiz atual e na legada. Como a
    /// chave muda a cada build, cada atualização do app abandonaria um diretório
    /// para sempre.
    /// </summary>
    public static void CleanupStaleWebUiCaches()
    {
        // A raiz legada sai por inteiro: nada mais é servido de lá.
        RemoverSubdiretorios(LegacyWebUiCacheRoot, exceto: null);
        RemoverSubdiretorios(WebUiCacheRoot, exceto: WebUiCacheKey);
    }

    private static void RemoverSubdiretorios(string raiz, string? exceto)
    {
        try
        {
            if (!Directory.Exists(raiz)) return;
            foreach (var dir in Directory.EnumerateDirectories(raiz))
            {
                if (exceto is not null &&
                    string.Equals(Path.GetFileName(dir), exceto, StringComparison.OrdinalIgnoreCase))
                    continue; // o cache em uso

                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex) { Debug.WriteLine($"CleanupStaleWebUiCaches: não removeu '{dir}': {ex.Message}"); }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"CleanupStaleWebUiCaches falhou em '{raiz}': {ex.Message}"); }
    }

    /// <summary>
    /// Extrai o bundle web para o cache e devolve o diretório servido ao WebView2.
    /// </summary>
    /// <remarks>
    /// O cache só é reaproveitado quando todos os arquivos conferem byte a byte
    /// com os recursos embarcados. Verificar apenas a presença do index.html
    /// deixaria o WebView2 servir um arquivo trocado por outro processo depois
    /// da extração; qualquer divergência força a reextração.
    /// </remarks>
    private static string ExtractWebUI()
    {
        var dir = Path.Combine(WebUiCacheRoot, WebUiCacheKey);
        var recursos = RecursosDaWebUi();

        if (CacheConfere(dir, recursos)) return dir;

        // Recomeça do zero: um cache parcial ou adulterado não é aproveitável.
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { Debug.WriteLine($"ExtractWebUI: não limpou '{dir}': {ex.Message}"); }

        var asm = Assembly.GetExecutingAssembly();
        foreach (var (recurso, relativo) in recursos)
        {
            var destino = Path.Combine(dir, relativo);
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            using var s = asm.GetManifestResourceStream(recurso)!;
            using var f = File.Create(destino);
            s.CopyTo(f);
        }
        return dir;
    }

    /// <summary>Recursos embarcados do bundle web e seu caminho relativo no disco.</summary>
    private static List<(string Recurso, string Relativo)> RecursosDaWebUi()
    {
        const string prefixo = "webui/";
        return Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(n => n.StartsWith(prefixo, StringComparison.Ordinal))
            .Select(n => (n, n[prefixo.Length..].Replace('/', Path.DirectorySeparatorChar)))
            .ToList();
    }

    /// <summary>
    /// Confere se o cache no disco corresponde exatamente aos recursos embarcados.
    /// Falha ao ler qualquer arquivo conta como divergência.
    /// </summary>
    private static bool CacheConfere(string dir, List<(string Recurso, string Relativo)> recursos)
    {
        if (!Directory.Exists(dir)) return false;

        try
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var (recurso, relativo) in recursos)
            {
                var destino = Path.Combine(dir, relativo);
                if (!File.Exists(destino)) return false;

                using var esperado = asm.GetManifestResourceStream(recurso)!;
                using var atual = File.OpenRead(destino);
                if (esperado.Length != atual.Length) return false;

                if (!SHA256.HashData(esperado).AsSpan().SequenceEqual(SHA256.HashData(atual)))
                    return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CacheConfere falhou em '{dir}': {ex.Message}");
            return false;
        }
    }

    private static Icon? LoadFormIcon()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
        if (name is null) return null;
        using var s = asm.GetManifestResourceStream(name);
        return s is null ? null : new Icon(s);
    }

    /// <summary>Overlay dark com spinner enquanto o WebView2 carrega.</summary>
    private sealed class LoadingOverlay : Panel
    {
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
        private float _angle;

        public LoadingOverlay()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = DarkBg;
            _timer.Tick += (_, _) => { _angle = (_angle + 9f) % 360f; Invalidate(); };
        }

        public void Start() { Visible = true; _timer.Start(); }
        public void Stop() { _timer.Stop(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int cx = Width / 2, cy = Height / 2 - 10, r = 16;
            // trilha
            using (var track = new Pen(Color.FromArgb(45, 45, 50), 3f))
                g.DrawEllipse(track, cx - r, cy - r, r * 2, r * 2);
            // arco (acento)
            using (var arc = new Pen(Color.FromArgb(47, 107, 255), 3f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                g.DrawArc(arc, cx - r, cy - r, r * 2, r * 2, _angle, 90f);
            using (var f = new Font("Segoe UI", 8.5f))
            using (var b = new SolidBrush(Color.FromArgb(140, 140, 150)))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString("Carregando…", f, b, cx, cy + r + 12, sf);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
