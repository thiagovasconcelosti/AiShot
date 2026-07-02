using System.Drawing;

namespace AiShot.Capture;

/// <summary>
/// Serviços que o editor de anotação consome via toolbar.
/// Implementado pela camada de wiring (Program/AppHost) e injetado no editor.
/// </summary>
public interface ICaptureServices
{
    /// <summary>Copia a imagem final (com anotações) para a área de transferência.</summary>
    void CopyToClipboard(Bitmap finalImage);

    /// <summary>Salva a imagem em arquivo (abre diálogo Salvar Como).</summary>
    void SaveToFile(Bitmap finalImage);

    /// <summary>Faz upload para o serviço de imagem grátis e devolve a URL.</summary>
    Task<string> UploadAsync(Bitmap finalImage, CancellationToken ct = default);

    /// <summary>Inicia uma conversa contínua (com histórico) sobre a imagem.</summary>
    AiShot.Ai.IAiChatSession StartChat(Bitmap finalImage);

    /// <summary>Se true, o overlay fecha automaticamente após copiar.</summary>
    bool CloseOnCopy { get; }
}
