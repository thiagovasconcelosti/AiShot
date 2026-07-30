using System.Windows.Forms;

namespace AiShot.App;

/// <summary>
/// Janela invisível que recebe mensagens do sistema em nome do app. O
/// <see cref="TrayAppContext"/> é um <see cref="ApplicationContext"/> e não tem
/// janela própria — sem isto, não haveria o que receber a difusão de
/// <see cref="SingleInstance.WmShowSettings"/>.
/// </summary>
/// <remarks>
/// É uma janela de topo (sem <c>Parent</c>), e não uma janela apenas-mensagem
/// (<c>HWND_MESSAGE</c>): a difusão para <c>HWND_BROADCAST</c> só alcança
/// janelas de topo. Fica invisível por nunca receber <c>WS_VISIBLE</c>, e fora
/// da barra de tarefas por <c>WS_EX_TOOLWINDOW</c>.
/// </remarks>
internal sealed class MessageWindow : NativeWindow, IDisposable
{
    private const int WsExToolWindow = 0x00000080;

    private readonly Action _onShowSettings;

    public MessageWindow(Action onShowSettings)
    {
        _onShowSettings = onShowSettings;
        CreateHandle(new CreateParams
        {
            Caption = "AiShot.MessageWindow",
            ExStyle = WsExToolWindow,
            // Sem Style: a ausência de WS_VISIBLE mantém a janela oculta.
        });
    }

    protected override void WndProc(ref Message m)
    {
        if ((uint)m.Msg == SingleInstance.WmShowSettings)
        {
            _onShowSettings();
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
