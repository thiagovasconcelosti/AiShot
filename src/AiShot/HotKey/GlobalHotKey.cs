using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AiShot.HotKey;

/// <summary>
/// Atalho global via hook de teclado low-level (WH_KEYBOARD_LL).
/// Mais robusto que RegisterHotKey: captura PrintScreen mesmo quando o Windows 11
/// o reserva para a Ferramenta de Captura, e suprime a tecla para o resto do
/// sistema (evita abrir o Snipping Tool junto).
/// </summary>
public sealed class GlobalHotKey : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    // Virtual-keys dos modificadores.
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12; // MENU = Alt
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // Mantém a delegate viva (senão o GC coleta e o hook quebra).
    private readonly HookProc _proc;
    private IntPtr _hook = IntPtr.Zero;

    private uint _targetVk;
    private bool _needCtrl, _needAlt, _needShift, _needWin;

    public event EventHandler? Pressed;

    /// <summary>
    /// Modo de captura (usado pela tela de Configurações): enquanto ativo, o
    /// atalho normal NÃO dispara; a tecla é suprimida (não abre Snipping) e
    /// enviada via <see cref="KeyCaptured"/> para o campo de configuração.
    /// </summary>
    public bool CaptureMode { get; set; }

    /// <summary>Tecla (não-modificadora) pressionada durante o modo de captura.</summary>
    public event Action<Keys>? KeyCaptured;

    public GlobalHotKey()
    {
        _proc = HookCallback;
    }

    /// <summary>Instala o hook para a tecla informada (ex.: "PrintScreen", "Ctrl+Alt+S").</summary>
    public bool Register(string hotKeyName)
    {
        Unregister();
        ParseHotKey(hotKeyName);
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook != IntPtr.Zero) StartWatchdog();
        return _hook != IntPtr.Zero;
    }

    public void Unregister()
    {
        StopWatchdog();
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    // ---------- Vigia do hook ----------
    //
    // O Windows desinstala hooks WH_KEYBOARD_LL cujo callback exceda
    // LowLevelHooksTimeout (~5 s por padrão). O handle continua não-nulo do
    // nosso lado, então não há como perguntar ao sistema se o hook ainda vale:
    // o atalho simplesmente para de funcionar, sem aviso.
    //
    // A verificação é indireta. Um hook já removido pelo sistema faz
    // UnhookWindowsHookEx falhar com ERROR_INVALID_HOOK_HANDLE (1404); um hook
    // vivo é removido com sucesso. Nos dois casos reinstalamos em seguida, de
    // modo que o efeito é o mesmo — a diferença serve só para avisar o usuário
    // uma única vez, na primeira queda observada.

    private const int ErrorInvalidHookHandle = 1404;

    private System.Windows.Forms.Timer? _watchdog;
    private bool _reportedFailure;

    /// <summary>Disparado quando o vigia detecta que o hook havia caído.</summary>
    public event Action? HookRecovered;

    private void StartWatchdog()
    {
        StopWatchdog();
        _watchdog = new System.Windows.Forms.Timer { Interval = 30_000 };
        _watchdog.Tick += (_, _) => VerifyHook();
        _watchdog.Start();
    }

    private void StopWatchdog()
    {
        if (_watchdog is null) return;
        _watchdog.Stop();
        _watchdog.Dispose();
        _watchdog = null;
    }

    /// <summary>Reinstala o hook e avisa se ele havia sido derrubado pelo sistema.</summary>
    private void VerifyHook()
    {
        if (_hook == IntPtr.Zero || CaptureMode) return;

        bool removed = UnhookWindowsHookEx(_hook);
        bool wasDead = !removed && Marshal.GetLastWin32Error() == ErrorInvalidHookHandle;

        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);

        if (wasDead && !_reportedFailure)
        {
            _reportedFailure = true; // avisa só na primeira vez, para não incomodar
            HookRecovered?.Invoke();
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                // Modo de captura: entrega a tecla ao campo de config e suprime.
                if (CaptureMode)
                {
                    var vk = (Keys)data.vkCode;
                    if (!IsModifierKey(vk))
                    {
                        var cb = KeyCaptured;
                        if (cb is not null)
                        {
                            var ctx = System.Threading.SynchronizationContext.Current;
                            if (ctx is not null) ctx.Post(_ => cb(vk), null);
                            else cb(vk);
                        }
                        return 1; // suprime a tecla principal (sem Snipping, sem captura)
                    }
                    return CallNextHookEx(_hook, nCode, wParam, lParam); // deixa modificador passar
                }

                if (data.vkCode == _targetVk && ModifiersMatch())
                {
                    // Dispara fora do contexto do hook para não travar a fila de teclado.
                    var ev = Pressed;
                    if (ev is not null)
                    {
                        System.Threading.SynchronizationContext.Current?.Post(_ => ev(this, EventArgs.Empty), null);
                        if (System.Threading.SynchronizationContext.Current is null)
                            ev(this, EventArgs.Empty);
                    }
                    // Suprime a tecla (impede o Snipping Tool de abrir junto).
                    return 1;
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsModifierKey(Keys vk) => vk is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.LWin or Keys.RWin;

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private bool ModifiersMatch()
    {
        bool ctrl = Down(VK_CONTROL);
        bool alt = Down(VK_MENU);
        bool shift = Down(VK_SHIFT);
        bool win = Down(VK_LWIN) || Down(VK_RWIN);
        return ctrl == _needCtrl && alt == _needAlt && shift == _needShift && win == _needWin;
    }

    /// <summary>Converte "Ctrl+Alt+PrintScreen" em vk alvo + flags de modificador.</summary>
    private void ParseHotKey(string name)
    {
        _needCtrl = _needAlt = _needShift = _needWin = false;
        Keys key = Keys.None;
        foreach (var raw in name.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": _needCtrl = true; break;
                case "alt": _needAlt = true; break;
                case "shift": _needShift = true; break;
                case "win": case "windows": _needWin = true; break;
                case "printscreen": case "prtsc": case "prtscr": key = Keys.PrintScreen; break;
                default:
                    if (Enum.TryParse<Keys>(raw, true, out var k)) key = k;
                    break;
            }
        }
        if (key == Keys.None) key = Keys.PrintScreen;
        _targetVk = (uint)key;
    }

    public void Dispose()
    {
        Unregister();
        Pressed = null;
        KeyCaptured = null;
        HookRecovered = null;
    }
}
