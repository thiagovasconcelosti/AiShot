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

    /// <summary>
    /// Caminho padrão do arquivo de config: %APPDATA%\AiShot\appsettings.json
    /// (fora da pasta do app; segredos cifrados via DPAPI).
    /// </summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AiShot", "appsettings.json");

    /// <summary>Local legado (ao lado do executável) — migrado no primeiro Load.</summary>
    private static string LegacyPath =>
        Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static AppConfig Load(string? path = null)
    {
        path ??= DefaultPath;

        AppConfig cfg;
        bool migrating = false;
        if (File.Exists(path))
        {
            cfg = Deserialize(path);
        }
        else if (File.Exists(LegacyPath))
        {
            // Migração: lê config antiga (texto puro ao lado do exe) e regrava
            // no novo local com os segredos cifrados.
            cfg = Deserialize(LegacyPath);
            migrating = true;
        }
        else
        {
            cfg = new AppConfig();
        }

        cfg.DecryptSecrets();
        if (migrating) cfg.Save(path); // persiste cifrado no novo local

        cfg.ApplyEnvironmentOverrides();
        return cfg;
    }

    private static AppConfig Deserialize(string p)
    {
        var json = File.ReadAllText(p);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Serializa uma cópia com os segredos cifrados (mantém as chaves em
        // claro na instância em memória para uso imediato).
        var json = JsonSerializer.Serialize(EncryptedClone(), JsonOpts);

        // Escrita atômica: grava em .tmp e troca, evitando corromper em crash.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Decifra todas as chaves para uso em memória.</summary>
    private void DecryptSecrets()
    {
        Ai.ApiKey = SecretProtector.Unprotect(Ai.ApiKey);
        if (Ai.Fallback is not null) Ai.Fallback.ApiKey = SecretProtector.Unprotect(Ai.Fallback.ApiKey);
        Ai.Vision.ApiKey = SecretProtector.Unprotect(Ai.Vision.ApiKey);
        ImageUpload.ApiKey = SecretProtector.Unprotect(ImageUpload.ApiKey);
    }

    /// <summary>Clone raso com as chaves cifradas (para persistir).</summary>
    private AppConfig EncryptedClone()
    {
        var c = new AppConfig
        {
            HotKey = HotKey,
            Ai = new AiConfig
            {
                Provider = Ai.Provider,
                ApiKey = SecretProtector.Protect(Ai.ApiKey),
                Model = Ai.Model,
                BaseUrl = Ai.BaseUrl,
                Vision = new VisionConfig
                {
                    Enabled = Ai.Vision.Enabled,
                    Provider = Ai.Vision.Provider,
                    ApiKey = SecretProtector.Protect(Ai.Vision.ApiKey),
                    Model = Ai.Vision.Model,
                    BaseUrl = Ai.Vision.BaseUrl,
                },
                Fallback = Ai.Fallback is null ? null : new AiEndpoint
                {
                    Provider = Ai.Fallback.Provider,
                    ApiKey = SecretProtector.Protect(Ai.Fallback.ApiKey),
                    Model = Ai.Fallback.Model,
                    BaseUrl = Ai.Fallback.BaseUrl,
                },
            },
            ImageUpload = new ImageUploadConfig
            {
                Service = ImageUpload.Service,
                ApiKey = SecretProtector.Protect(ImageUpload.ApiKey),
            },
        };
        return c;
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
