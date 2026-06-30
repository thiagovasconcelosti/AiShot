using System.Net.Http;
using AiShot.Ai.Providers;

namespace AiShot.Ai;

/// <summary>
/// Fábrica que cria o <see cref="IAiProvider"/> apropriado pelo nome do provider.
/// </summary>
public static class AiProviderFactory
{
    /// <summary>
    /// Cria um provider. "anthropic"/"openai" (case-insensitive); default anthropic.
    /// </summary>
    public static IAiProvider Create(string provider, string apiKey, string model, string baseUrl, HttpClient http)
    {
        return (provider ?? "").Trim().ToLowerInvariant() switch
        {
            "openai" => new OpenAiProvider(apiKey, model, baseUrl, http),
            "anthropic" => new AnthropicProvider(apiKey, model, baseUrl, http),
            _ => new AnthropicProvider(apiKey, model, baseUrl, http),
        };
    }
}
