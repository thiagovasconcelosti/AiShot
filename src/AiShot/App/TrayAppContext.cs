using System.Diagnostics;
using System.Windows.Forms;
using AiShot.Capture;
using AiShot.Config;
using AiShot.HotKey;
using AiShot.Settings;

namespace AiShot.App;

/// <summary>
/// Contexto da aplicação no tray. Sem janela principal: ícone na bandeja,
/// menu de contexto e atalho global para iniciar a captura.
/// </summary>
public sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly GlobalHotKey _hotKey;
    private readonly HttpClient _http = HttpClientFactory.Create();
    private AppConfig _cfg;
    private AppHost _host;
    private CaptureOverlay? _overlay;
    private readonly MessageWindow _msgWindow;

    /// <summary>
    /// Referência ao diálogo de Configurações enquanto ele está aberto, usada
    /// apenas para trazê-lo ao foco. O dono é a instrução <c>using</c> em
    /// <see cref="OpenSettings"/>, que o descarta ao fechar — este campo é um
    /// apelido temporário e descartá-lo aqui seria descarte em duplicidade.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA2213:Campos descartáveis devem ser descartados",
        Justification = "Apelido de uma variável using local; o descarte é feito por OpenSettings.")]
    private SettingsForm? _settingsForm;

    private bool _settingsOpen;

    public TrayAppContext()
    {
        _cfg = AppConfig.Load();
        _host = new AppHost(_cfg, _http);

        // Recebe o aviso de uma segunda instância pedindo para mostrar a UI.
        _msgWindow = new MessageWindow(OpenSettings);

        _tray = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = $"AiShot v{UpdateService.Current}",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => StartCapture();

        _hotKey = new GlobalHotKey();
        _hotKey.Pressed += (_, _) => StartCapture();
        _hotKey.HookRecovered += OnHookRecovered;
        RegisterHotKey();

        CleanupTempFiles();
        Settings.SettingsForm.CleanupStaleWebUiCaches(); // caches de builds antigos
        Settings.SettingsForm.Prewarm(); // aquece o WebView2 pra abrir Configurações rápido
    }

    /// <summary>Remove PNGs temporários do app (abertos no Paint) com mais de 1h.</summary>
    private static void CleanupTempFiles()
    {
        try
        {
            var cutoff = DateTime.Now.AddHours(-1);
            foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), "aishot_*.png"))
            {
                try { if (File.GetLastWriteTime(f) < cutoff) File.Delete(f); }
                catch (Exception ex) { Debug.WriteLine($"CleanupTempFiles: não removeu '{f}': {ex.Message}"); }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"CleanupTempFiles falhou: {ex.Message}"); }
    }

    private static Icon LoadAppIcon()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
        if (name is not null)
        {
            using var s = asm.GetManifestResourceStream(name);
            if (s is not null) return new Icon(s);
        }
        return SystemIcons.Application;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Capturar", null, (_, _) => StartCapture());
        menu.Items.Add("Configurações…", null, (_, _) => OpenSettings());

        var startup = new ToolStripMenuItem("Iniciar com o Windows")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled(),
        };
        startup.CheckedChanged += (s, _) =>
        {
            try { StartupManager.SetEnabled(((ToolStripMenuItem)s!).Checked); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "AiShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitApp());
        return menu;
    }

    /// <summary>
    /// O vigia reinstalou o hook após o Windows tê-lo derrubado (callback lento
    /// além de LowLevelHooksTimeout). Avisa uma única vez — a classe do atalho
    /// só dispara este evento na primeira queda observada.
    /// </summary>
    private void OnHookRecovered() =>
        _tray.ShowBalloonTip(4000, "AiShot",
            $"O atalho '{_cfg.HotKey}' havia parado de responder e foi restabelecido.",
            ToolTipIcon.Info);

    private void RegisterHotKey()
    {
        if (!_hotKey.Register(_cfg.HotKey))
        {
            _tray.ShowBalloonTip(3000, "AiShot",
                $"Não foi possível registrar o atalho '{_cfg.HotKey}'. Pode estar em uso.",
                ToolTipIcon.Warning);
        }
    }

    private void StartCapture()
    {
        // Show() não bloqueia, então uma flag zerada no finally não protegeria
        // nada: a guarda é a própria referência do overlay, viva até ele fechar.
        if (_overlay is not null && !_overlay.IsDisposed)
        {
            _overlay.Activate();
            return;
        }

        try
        {
            var overlay = new CaptureOverlay(_host);
            overlay.FormClosed += (s, _) =>
            {
                if (ReferenceEquals(_overlay, s)) _overlay = null;
            };
            _overlay = overlay;
            overlay.Show();
            overlay.Activate();
        }
        catch (Exception ex)
        {
            _overlay = null;
            MessageBox.Show(ex.Message, "AiShot — erro na captura",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenSettings()
    {
        // ShowDialog é modal nesta thread, mas OpenSettings também é acionado
        // por mensagem de janela (segunda instância): sem a guarda, dois
        // diálogos poderiam ser empilhados.
        if (_settingsOpen)
        {
            _settingsForm?.Activate();
            return;
        }

        _settingsOpen = true;
        try
        {
            using var form = new SettingsForm(_cfg, _hotKey, _http);
            _settingsForm = form;
            if (form.ShowDialog() == DialogResult.OK)
            {
                // Recarrega config + recria host e re-registra atalho.
                _cfg = AppConfig.Load();
                _host = new AppHost(_cfg, _http);
                RegisterHotKey();
            }
        }
        finally
        {
            _settingsForm = null;
            _settingsOpen = false;
        }
    }

    private void ExitApp()
    {
        Dispose(true);
        ExitThread();
    }

    /// <summary>
    /// Libera os recursos do contexto. Sobrescrito porque o
    /// <see cref="ApplicationContext"/> é descartável e pode ser encerrado por
    /// caminhos que não passam pelo item "Sair" do menu — antes, a liberação
    /// vivia só ali e o overlay ativo nunca era descartado.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_descartado)
        {
            _descartado = true;

            _hotKey.HookRecovered -= OnHookRecovered;
            _hotKey.Dispose();
            _msgWindow.Dispose();

            _overlay?.Dispose();
            _overlay = null;

            _tray.Visible = false;
            _tray.Dispose();
            _http.Dispose();
        }
        base.Dispose(disposing);
    }

    private bool _descartado;
}
