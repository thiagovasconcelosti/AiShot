using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;
using AiShot.Capture;
using AiShot.Config;
using AiShot.History;
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
    /// Submenu do histórico. O <see cref="ContextMenuStrip"/> que o contém é
    /// dono dos seus itens e os descarta junto; guardar a referência aqui é só
    /// para repovoá-lo a cada abertura.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA2213:Campos descartáveis devem ser descartados",
        Justification = "Item de um ContextMenuStrip, que é dono dos filhos e os descarta.")]
    private ToolStripMenuItem? _historyMenu;

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

        // Preenchido na abertura: o conteúdo muda a cada captura, e montar aqui
        // deixaria o menu mostrando o estado de quando o app subiu.
        _historyMenu = new ToolStripMenuItem("Histórico");
        menu.Items.Add(_historyMenu);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitApp());

        menu.Opening += (_, _) => PreencherHistorico();
        return menu;
    }

    /// <summary>
    /// Monta o submenu do histórico com as capturas atuais. Some do menu quando
    /// o recurso está desligado — um item vazio sugeriria que há algo guardado.
    /// </summary>
    private void PreencherHistorico()
    {
        if (_historyMenu is null) return;

        // Descarta os itens da montagem anterior antes de limpar: Clear() só
        // solta as referências, e as miniaturas são bitmaps que ficariam
        // acumulando a cada abertura do menu.
        // A cópia é necessária: Dispose remove o item da coleção, e iterar
        // sobre ela enquanto encolhe pularia metade dos itens.
        var anteriores = _historyMenu.DropDownItems.Cast<ToolStripItem>().ToArray();
        _historyMenu.DropDownItems.Clear();
        foreach (var antigo in anteriores)
        {
            antigo.Image?.Dispose();
            antigo.Dispose();
        }

        if (!_cfg.History.Enabled)
        {
            _historyMenu.Visible = false;
            return;
        }

        _historyMenu.Visible = true;

        var historico = new CaptureHistory(
            CaptureHistory.PastaPadrao, _cfg.History.MaxItems, _cfg.History.MaxSizeMb);

        var itens = historico.Listar();
        if (itens.Count == 0)
        {
            _historyMenu.DropDownItems.Add(new ToolStripMenuItem("(vazio)") { Enabled = false });
            return;
        }

        foreach (var item in itens)
        {
            var entrada = new ToolStripMenuItem(
                item.Momento.ToLocalTime().ToString("dd/MM HH:mm:ss", CultureInfo.CurrentCulture))
            {
                Image = CarregarMiniatura(item.Caminho),
                Tag = item.Caminho,
            };
            entrada.Click += (s, _) => RecuperarCaptura((string)((ToolStripMenuItem)s!).Tag!);
            _historyMenu.DropDownItems.Add(entrada);
        }

        _historyMenu.DropDownItems.Add(new ToolStripSeparator());
        _historyMenu.DropDownItems.Add("Abrir pasta", null, (_, _) => AbrirPastaDoHistorico());
        _historyMenu.DropDownItems.Add("Limpar histórico", null, (_, _) => LimparHistorico(historico));
    }

    /// <summary>
    /// Miniatura para o item do menu. Devolve null se o arquivo não puder ser
    /// lido — o item continua clicável, só sem imagem.
    /// </summary>
    private static Image? CarregarMiniatura(string caminho)
    {
        try
        {
            // Lê os bytes antes de decodificar: Image.FromFile mantém o arquivo
            // travado enquanto a imagem viver, e o menu guarda a miniatura.
            using var original = Image.FromStream(new MemoryStream(File.ReadAllBytes(caminho)));
            return new Bitmap(original, new Size(32, 32));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Histórico: miniatura de '{caminho}' falhou: {ex.Message}");
            return null;
        }
    }

    /// <summary>Copia a captura guardada de volta para a área de transferência.</summary>
    private void RecuperarCaptura(string caminho)
    {
        try
        {
            using var img = Image.FromStream(new MemoryStream(File.ReadAllBytes(caminho)));
            Clipboard.SetImage(img);
            _tray.ShowBalloonTip(2000, "AiShot", "Captura copiada para a área de transferência.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível recuperar a captura.\n\n{ex.Message}",
                "AiShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void AbrirPastaDoHistorico()
    {
        try
        {
            Directory.CreateDirectory(CaptureHistory.PastaPadrao);
            Process.Start(new ProcessStartInfo(CaptureHistory.PastaPadrao) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AiShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Apaga tudo, com confirmação. É irreversível e a única cópia de uma
    /// captura que o usuário não salvou pode estar aqui.
    /// </summary>
    private void LimparHistorico(CaptureHistory historico)
    {
        var resposta = MessageBox.Show(
            "Apagar todas as capturas guardadas no histórico?\n\nEsta ação não pode ser desfeita.",
            "AiShot", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

        if (resposta != DialogResult.Yes) return;

        try { historico.Limpar(); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AiShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
