using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AiShot.App;

/// <summary>Info da última versão disponível no GitHub Releases.</summary>
public sealed record UpdateInfo(string Version, string Url);

/// <summary>
/// Verifica se há versão mais nova no GitHub Releases e baixa/roda o instalador.
/// </summary>
public static class UpdateService
{
    private const string LatestApi =
        "https://api.github.com/repos/thiagovasconcelosti/AiShot/releases/latest";

    /// <summary>Versão atual do app (Major.Minor.Build).</summary>
    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>Retorna a versão nova (se houver) ou null se já está atualizado/erro.</summary>
    public static async Task<UpdateInfo?> CheckAsync(HttpClient http, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, LatestApi);
            req.Headers.UserAgent.ParseAdd("AiShot-Updater");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;

            var tag = (root.TryGetProperty("tag_name", out var t) ? t.GetString() : null)?.TrimStart('v', 'V');
            if (tag is null || !Version.TryParse(tag, out var parsed)) return null;
            var latest = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            if (latest <= Current) return null;

            // Procura o asset do instalador (AiShot-Setup-*.exe).
            if (!root.TryGetProperty("assets", out var assets)) return null;
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (name.StartsWith("AiShot-Setup", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var url = a.GetProperty("browser_download_url").GetString();
                    if (IsTrustedUrl(url)) return new UpdateInfo(tag, url!);
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            // Checagem de update é best-effort (offline, rate limit, etc.): não interrompe
            // o app, mas registra o motivo em vez de engolir cego.
            Debug.WriteLine($"UpdateService.CheckAsync falhou: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Só confia em URLs HTTPS do GitHub (host do release/assets). Evita rodar
    /// binário de origem inesperada mesmo que a resposta da API fosse adulterada.
    /// </summary>
    private static bool IsTrustedUrl(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttps) return false;
        var host = u.Host.ToLowerInvariant();
        return host == "github.com"
            || host.EndsWith(".github.com", StringComparison.Ordinal)
            || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
    }

    /// <summary>Baixa o instalador e o executa (o Inno atualiza sobre a instalação atual).</summary>
    public static async Task DownloadAndRunAsync(HttpClient http, string url, CancellationToken ct = default)
    {
        if (!IsTrustedUrl(url)) throw new InvalidOperationException("URL de atualização não confiável.");
        var tmp = Path.Combine(Path.GetTempPath(), "AiShot-Update-Setup.exe");
        using (var s = await http.GetStreamAsync(url, ct).ConfigureAwait(false))
        using (var f = File.Create(tmp))
            await s.CopyToAsync(f, ct).ConfigureAwait(false);

        Process.Start(new ProcessStartInfo { FileName = tmp, UseShellExecute = true });
    }
}
