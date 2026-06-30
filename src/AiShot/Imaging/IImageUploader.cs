namespace AiShot.Imaging;

/// <summary>Resultado de upload para serviço de imagem grátis.</summary>
public sealed record UploadResult(string PageUrl, string DirectUrl, string DeleteUrl);

/// <summary>Faz upload de PNG para serviço grátis (freeimage.host / imgbb).</summary>
public interface IImageUploader
{
    string Name { get; }
    Task<UploadResult> UploadAsync(byte[] imagePng, CancellationToken ct = default);
}
