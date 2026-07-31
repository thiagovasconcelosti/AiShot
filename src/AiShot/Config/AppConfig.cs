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

    /// <summary>
    /// Idioma da interface: "auto" (segue o sistema), "pt", "en" ou "es".
    /// Idiomas sem tradução caem no português, o idioma-fonte do projeto.
    /// </summary>
    public string Language { get; set; } = Resources.Idioma.Automatico;
    /// <summary>Fecha o overlay automaticamente após copiar a imagem.</summary>
    public bool CloseOnCopy { get; set; } = false;
    public AiConfig Ai { get; set; } = new();
    public ImageUploadConfig ImageUpload { get; set; } = new();
    public HistoryConfig History { get; set; } = new();

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

    /// <summary>
    /// Carrega a configuração. Sem <paramref name="path"/>, usa
    /// <see cref="DefaultPath"/> e migra a configuração legada (ao lado do
    /// executável) caso o arquivo novo ainda não exista.
    /// </summary>
    /// <param name="path">
    /// Caminho explícito. Quando informado, a migração legada NÃO se aplica: o
    /// chamador pediu um arquivo específico, e ler outro no lugar seria uma
    /// surpresa — se ele não existir, valem os padrões.
    /// </param>
    public static AppConfig Load(string? path = null)
    {
        bool caminhoPadrao = path is null;
        path ??= DefaultPath;

        AppConfig cfg;
        bool migrating = false;
        if (File.Exists(path))
        {
            cfg = Deserialize(path);
        }
        else if (caminhoPadrao && File.Exists(LegacyPath))
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
        // File.Replace preserva um backup da versão anterior e faz a troca no
        // nível do sistema de arquivos; só serve quando o destino já existe.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
        else File.Move(tmp, path);
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
            Language = Language,
            CloseOnCopy = CloseOnCopy,
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
            History = new HistoryConfig
            {
                Enabled = History.Enabled,
                MaxItems = History.MaxItems,
                MaxSizeMb = History.MaxSizeMb,
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
        Language = E("LANGUAGE") ?? Language;
        var closeCopy = E("CLOSEONCOPY");
        if (closeCopy is not null && bool.TryParse(closeCopy, out var cc)) CloseOnCopy = cc;

        Ai.Provider = E("AI__PROVIDER") ?? Ai.Provider;
        Ai.ApiKey = E("AI__APIKEY") ?? Ai.ApiKey;
        Ai.Model = E("AI__MODEL") ?? Ai.Model;
        Ai.BaseUrl = E("AI__BASEURL") ?? Ai.BaseUrl;

        if (Ai.Fallback is not null)
        {
            Ai.Fallback.Provider = E("AI__FALLBACK__PROVIDER") ?? Ai.Fallback.Provider;
            Ai.Fallback.ApiKey = E("AI__FALLBACK__APIKEY") ?? Ai.Fallback.ApiKey;
            Ai.Fallback.Model = E("AI__FALLBACK__MODEL") ?? Ai.Fallback.Model;
            Ai.Fallback.BaseUrl = E("AI__FALLBACK__BASEURL") ?? Ai.Fallback.BaseUrl;
        }

        var visEnabled = E("AI__VISION__ENABLED");
        if (visEnabled is not null && bool.TryParse(visEnabled, out var ve))
            Ai.Vision.Enabled = ve;
        Ai.Vision.Provider = E("AI__VISION__PROVIDER") ?? Ai.Vision.Provider;
        Ai.Vision.ApiKey = E("AI__VISION__APIKEY") ?? Ai.Vision.ApiKey;
        Ai.Vision.Model = E("AI__VISION__MODEL") ?? Ai.Vision.Model;
        Ai.Vision.BaseUrl = E("AI__VISION__BASEURL") ?? Ai.Vision.BaseUrl;

        ImageUpload.Service = E("IMAGEUPLOAD__SERVICE") ?? ImageUpload.Service;
        ImageUpload.ApiKey = E("IMAGEUPLOAD__APIKEY") ?? ImageUpload.ApiKey;

        var histEnabled = E("HISTORY__ENABLED");
        if (histEnabled is not null && bool.TryParse(histEnabled, out var he)) History.Enabled = he;
        var histItems = E("HISTORY__MAXITEMS");
        if (histItems is not null && int.TryParse(histItems, out var hi)) History.MaxItems = hi;
        var histSize = E("HISTORY__MAXSIZEMB");
        if (histSize is not null && int.TryParse(histSize, out var hs)) History.MaxSizeMb = hs;
    }
}

public sealed class AiConfig
{
    /// <summary>"anthropic" ou "openai".</summary>
    public string Provider { get; set; } = "anthropic";
    public string ApiKey { get; set; } = "";
    /// <summary>
    /// Identificador do modelo. Acompanha as versões publicadas pelo provedor e
    /// precisa ser revisado periodicamente — um identificador retirado do ar faz
    /// a API responder 404 na primeira captura.
    /// </summary>
    public string Model { get; set; } = "claude-opus-5";
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

/// <summary>Histórico das últimas capturas em disco.</summary>
public sealed class HistoryConfig
{
    /// <summary>
    /// Desligado por padrão. Uma captura carrega o que estava na tela — senhas
    /// à mostra, conversas, documentos —, então gravá-la em disco é uma escolha
    /// que o usuário faz, não um comportamento que ele descobre depois.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Quantidade máxima de capturas guardadas.</summary>
    public int MaxItems { get; set; } = 10;

    /// <summary>Espaço máximo ocupado pelo histórico, em megabytes.</summary>
    public int MaxSizeMb { get; set; } = 100;
}
