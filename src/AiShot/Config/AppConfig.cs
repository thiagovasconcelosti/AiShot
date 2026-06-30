using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiShot.Config;

/// <summary>
/// Configuração raiz do app. Carregada de appsettings.json com override por
/// variáveis de ambiente (prefixo AISHOT_).
/// </summary>
public sealed class AppConfig
{
    public string HotKey { get; set; } = "PrintScreen";
    public AiConfig Ai { get; set; } = new();
    public ImageUploadConfig ImageUpload { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Caminho padrão do arquivo de config (ao lado do executável).</summary>
    public static string DefaultPath =>
        Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static AppConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        AppConfig cfg;
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
        }
        else
        {
            cfg = new AppConfig();
        }
        cfg.ApplyEnvironmentOverrides();
        return cfg;
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
    }

    /// <summary>
    /// Override por env var. Ex.: AISHOT_AI__PROVIDER, AISHOT_AI__APIKEY,
    /// AISHOT_AI__VISION__ENABLED, AISHOT_IMAGEUPLOAD__APIKEY.
    /// </summary>
    public void ApplyEnvironmentOverrides()
    {
        string? E(string k) => Environment.GetEnvironmentVariable("AISHOT_" + k);

        HotKey = E("HOTKEY") ?? HotKey;

        Ai.Provider = E("AI__PROVIDER") ?? Ai.Provider;
        Ai.ApiKey = E("AI__APIKEY") ?? Ai.ApiKey;
        Ai.Model = E("AI__MODEL") ?? Ai.Model;
        Ai.BaseUrl = E("AI__BASEURL") ?? Ai.BaseUrl;

        if (Ai.Fallback is not null)
        {
            Ai.Fallback.Provider = E("AI__FALLBACK__PROVIDER") ?? Ai.Fallback.Provider;
            Ai.Fallback.ApiKey = E("AI__FALLBACK__APIKEY") ?? Ai.Fallback.ApiKey;
            Ai.Fallback.Model = E("AI__FALLBACK__MODEL") ?? Ai.Fallback.Model;
        }

        var visEnabled = E("AI__VISION__ENABLED");
        if (visEnabled is not null && bool.TryParse(visEnabled, out var ve))
            Ai.Vision.Enabled = ve;
        Ai.Vision.Provider = E("AI__VISION__PROVIDER") ?? Ai.Vision.Provider;
        Ai.Vision.ApiKey = E("AI__VISION__APIKEY") ?? Ai.Vision.ApiKey;
        Ai.Vision.Model = E("AI__VISION__MODEL") ?? Ai.Vision.Model;

        ImageUpload.Service = E("IMAGEUPLOAD__SERVICE") ?? ImageUpload.Service;
        ImageUpload.ApiKey = E("IMAGEUPLOAD__APIKEY") ?? ImageUpload.ApiKey;
    }
}

public sealed class AiConfig
{
    /// <summary>"anthropic" ou "openai".</summary>
    public string Provider { get; set; } = "anthropic";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-opus-4-8";
    /// <summary>Override opcional da URL base da API.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>IA de fallback usada se a principal falhar.</summary>
    public AiEndpoint? Fallback { get; set; } = new()
    {
        Provider = "openai",
        ApiKey = "",
        Model = "gpt-4o",
    };

    /// <summary>IA de visão (opcional). Roda ANTES da principal para descrever a imagem.</summary>
    public VisionConfig Vision { get; set; } = new();
}

public class AiEndpoint
{
    public string Provider { get; set; } = "anthropic";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string BaseUrl { get; set; } = "";
}

public sealed class VisionConfig : AiEndpoint
{
    public bool Enabled { get; set; } = false;
}

public sealed class ImageUploadConfig
{
    /// <summary>"freeimage" (freeimage.host) ou "imgbb".</summary>
    public string Service { get; set; } = "freeimage";
    public string ApiKey { get; set; } = "";
}
