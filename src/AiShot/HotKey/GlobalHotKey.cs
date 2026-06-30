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
        return _hook != IntPtr.Zero;
    }

    public void Unregister()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
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

    public void Dispose() => Unregister();
}
