using System.Drawing;
using AiShot.Ai;
using AiShot.Capture;

namespace AiShot.Tests;

/// <summary>
/// Verificação de fumaça do overlay: construir, exibir, pintar e fechar sem
/// exceção.
/// </summary>
/// <remarks>
/// <para>
/// Não valida aparência — valida que o caminho de pintura executa. A separação
/// entre AnnotationController, OverlayChrome, OverlayActions e ToolbarRenderer
/// mexeu justamente nesse caminho, e um erro ali só apareceria ao abrir a
/// captura de verdade.
/// </para>
/// <para>
/// <b>Alcance:</b> cobre o overlay no estado inicial, com a seleção ainda
/// vazia. Os trechos que dependem de uma seleção ativa — barras de ferramentas,
/// paleta, menu de espessura, dimensões — não são exercitados, porque
/// <c>OnPaint</c> retorna antes deles. Verificado plantando uma exceção em cada
/// ponto: no início de <c>OnPaint</c> o teste falha; dentro de
/// <c>DrawDimensions</c> ele passa. Cobrir o modo de edição exigiria simular o
/// arraste do mouse, o que fica para quando houver necessidade.
/// </para>
/// <para>
/// Precisa de STA (Single-Threaded Apartment) e de uma sessão com área de
/// trabalho: os testes são ignorados quando não houver uma.
/// </para>
/// </remarks>
[Collection("interface")]
public class CaptureOverlaySmokeTests
{
    /// <summary>Serviços de mentira: registram o pedido sem tocar em rede ou disco.</summary>
    private sealed class ServicosFalsos : ICaptureServices
    {
        public int Copias { get; private set; }
        public Size UltimoTamanho { get; private set; }

        public bool CloseOnCopy => false;

        public void CopyToClipboard(Bitmap finalImage)
        {
            Copias++;
            UltimoTamanho = finalImage.Size;
        }

        public void SaveToFile(Bitmap finalImage) { }

        public Task<string> UploadAsync(Bitmap finalImage, CancellationToken ct = default) =>
            Task.FromResult("https://exemplo.invalido/imagem.png");

        public IAiChatSession StartChat(Bitmap finalImage) =>
            throw new NotSupportedException("O chat não é exercitado nesta verificação.");
    }

    /// <summary>
    /// Executa a ação numa thread STA. O WinForms exige esse modelo, e o xUnit
    /// roda os testes em MTA (Multi-Threaded Apartment) por padrão.
    /// </summary>
    private static void EmStaThread(Action acao)
    {
        Exception? falha = null;
        var t = new Thread(() =>
        {
            try { acao(); }
            catch (Exception ex) { falha = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();

        Assert.True(t.Join(TimeSpan.FromSeconds(30)), "A verificação do overlay não terminou a tempo.");
        if (falha is not null) throw new InvalidOperationException("O overlay falhou: " + falha.Message, falha);
    }

    /// <summary>
    /// Uma sessão sem área de trabalho (serviço, contêiner) não consegue criar
    /// janelas nem copiar a tela.
    /// </summary>
    private static bool TemAreaDeTrabalho()
    {
        try { return System.Windows.Forms.SystemInformation.VirtualScreen.Width > 0; }
        catch { return false; }
    }

    [SkippableFact]
    public void Overlay_Constroi_Exibe_E_Fecha_SemExcecao()
    {
        Skip.IfNot(TemAreaDeTrabalho(), "Requer uma sessão com área de trabalho.");

        EmStaThread(() =>
        {
            using var overlay = new CaptureOverlay(new ServicosFalsos());

            Assert.True(overlay.Bounds.Width > 0);
            Assert.True(overlay.Bounds.Height > 0);

            overlay.Show();
            System.Windows.Forms.Application.DoEvents();
            overlay.Close();
        });
    }

    [SkippableFact]
    public void Overlay_PintaOCaminhoCompleto_SemExcecao()
    {
        Skip.IfNot(TemAreaDeTrabalho(), "Requer uma sessão com área de trabalho.");

        EmStaThread(() =>
        {
            using var overlay = new CaptureOverlay(new ServicosFalsos());
            overlay.Show();
            System.Windows.Forms.Application.DoEvents();

            // A pintura é invocada diretamente, e não por Refresh(): o WinForms
            // enfileira o repaint e executa na bomba de mensagens, onde uma
            // exceção escaparia deste try e o teste passaria mesmo quebrado.
            using var bmp = new Bitmap(overlay.Width, overlay.Height);
            using var g = Graphics.FromImage(bmp);
            using var args = new System.Windows.Forms.PaintEventArgs(g, overlay.ClientRectangle);

            var onPaint = typeof(System.Windows.Forms.Control).GetMethod(
                "OnPaint",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?? throw new InvalidOperationException("OnPaint não encontrado.");

            try
            {
                onPaint.Invoke(overlay, [args]);
            }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }

            overlay.Close();
        });
    }

    [SkippableFact]
    public void Overlay_DescartadoDuasVezes_NaoLanca()
    {
        Skip.IfNot(TemAreaDeTrabalho(), "Requer uma sessão com área de trabalho.");

        EmStaThread(() =>
        {
            var overlay = new CaptureOverlay(new ServicosFalsos());
            overlay.Dispose();
            overlay.Dispose(); // idempotente: o cancelamento compartilhado já foi descartado
        });
    }
}
