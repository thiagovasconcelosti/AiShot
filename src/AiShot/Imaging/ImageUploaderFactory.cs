using AiShot.Config;

namespace AiShot.Imaging;

// Fábrica que escolhe o uploader conforme o serviço configurado.
public static class ImageUploaderFactory
{
    public static IImageUploader Create(ImageUploadConfig cfg, HttpClient http)
    {
        // Seleção por nome do serviço, ignorando maiúsculas/minúsculas.
        return (cfg.Service ?? "").Trim().ToLowerInvariant() switch
        {
            "imgbb" => new ImgbbUploader(cfg.ApiKey, http),
            _ => new FreeImageUploader(cfg.ApiKey, http),
        };
    }
}
