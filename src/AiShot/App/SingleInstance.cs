using System.Runtime.InteropServices;

namespace AiShot.App;

/// <summary>
/// Garante uma única instância do app por sessão de usuário. Duas instâncias
/// registrariam dois hooks globais na mesma tecla e exibiriam dois ícones na
/// bandeja.
/// </summary>
/// <remarks>
/// O escopo é <c>Local\</c> (por sessão), não <c>Global\</c>: sessões distintas
/// do Terminal Services são usuários distintos e podem rodar o app em paralelo,
/// cada uma com seu hook e sua bandeja.
/// </remarks>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\AiShot.SingleInstance.v1";

    /// <summary>
    /// Mensagem de janela usada para acordar a instância viva. Registrada no
    /// sistema — o mesmo nome devolve o mesmo identificador em qualquer processo.
    /// </summary>
    public static readonly uint WmShowSettings = RegisterWindowMessage("AiShot.ShowSettings.v1");

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Difunde para todas as janelas de topo do sistema.</summary>
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    private Mutex? _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// Tenta assumir a instância única. Devolve <c>null</c> se outra já estiver
    /// rodando — nesse caso a instância viva é notificada e o chamador deve sair.
    /// </summary>
    public static SingleInstance? TryAcquire()
    {
        // createdNew distingue "criei o mutex" de "abri um que já existia".
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew) return new SingleInstance(mutex);

        mutex.Dispose();
        NotifyExistingInstance();
        return null;
    }

    /// <summary>
    /// Pede à instância viva que mostre as Configurações. Difundido porque o
    /// handle da janela dela é desconhecido; só quem registrou a mesma mensagem
    /// a reconhece, e as demais janelas do sistema a ignoram.
    /// </summary>
    private static void NotifyExistingInstance() =>
        PostMessage(HwndBroadcast, WmShowSettings, IntPtr.Zero, IntPtr.Zero);

    public void Dispose()
    {
        if (_mutex is null) return;
        try { _mutex.ReleaseMutex(); }
        catch (ApplicationException) { /* não somos o dono — nada a liberar */ }
        _mutex.Dispose();
        _mutex = null;
    }
}
