using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace AiShot.App;

/// <summary>
/// Info da última versão disponível no GitHub Releases.
/// </summary>
/// <param name="Version">Versão da tag, sem o "v" inicial.</param>
/// <param name="Url">URL do instalador.</param>
/// <param name="ChecksumUrl">
/// URL do arquivo .sha256 correspondente, quando o release o publica. Nulo em
/// releases antigos, anteriores à automação do fluxo de publicação.
/// </param>
public sealed record UpdateInfo(string Version, string Url, string? ChecksumUrl = null);

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
            using var cts = HttpUtil.Timeout(ct, TimeSpan.FromSeconds(30));
            using var resp = await http.SendAsync(req, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false));
            var root = doc.RootElement;

            var tag = (root.TryGetProperty("tag_name", out var t) ? t.GetString() : null)?.TrimStart('v', 'V');
            if (tag is null || !Version.TryParse(tag, out var parsed)) return null;
            var latest = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            if (latest <= Current) return null;

            // Procura o asset do instalador (AiShot-Setup-*.exe) e o .sha256 par.
            if (!root.TryGetProperty("assets", out var assets)) return null;

            string? instalador = null, checksum = null;
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (!name.StartsWith("AiShot-Setup", StringComparison.OrdinalIgnoreCase)) continue;

                var url = a.GetProperty("browser_download_url").GetString();
                if (!IsTrustedUrl(url)) continue;

                // A ordem importa: ".exe.sha256" também termina em ".sha256",
                // então o checksum precisa ser testado antes do executável.
                if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) checksum ??= url;
                else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) instalador ??= url;
            }

            return instalador is null ? null : new UpdateInfo(tag, instalador, checksum);
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
    internal static bool IsTrustedUrl(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttps) return false;
        var host = u.Host.ToLowerInvariant();
        return host == "github.com"
            || host.EndsWith(".github.com", StringComparison.Ordinal)
            || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
    }

    /// <summary>
    /// Baixa o instalador, confere a integridade e o executa (o Inno atualiza
    /// sobre a instalação atual).
    /// </summary>
    /// <remarks>
    /// Quando o release publica o arquivo .sha256, o instalador só roda se o
    /// hash bater. Um arquivo que não confere é apagado e a atualização é
    /// abortada — executar um binário adulterado é pior do que não atualizar.
    /// </remarks>
    public static async Task DownloadAndRunAsync(
        HttpClient http, string url, string? checksumUrl = null, CancellationToken ct = default)
    {
        if (!IsTrustedUrl(url)) throw new InvalidOperationException("URL de atualização não confiável.");
        if (checksumUrl is not null && !IsTrustedUrl(checksumUrl))
            throw new InvalidOperationException("URL do checksum não confiável.");

        var tmp = Path.Combine(Path.GetTempPath(), "AiShot-Update-Setup.exe");

        // Download do instalador pode ser grande: timeout generoso, mas ainda limitado.
        using var cts = HttpUtil.Timeout(ct, TimeSpan.FromMinutes(10));
        using (var s = await http.GetStreamAsync(url, cts.Token).ConfigureAwait(false))
        using (var f = File.Create(tmp))
            await s.CopyToAsync(f, cts.Token).ConfigureAwait(false);

        try
        {
            await VerificarIntegridadeAsync(http, tmp, checksumUrl, cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // Não deixa para trás um executável que falhou na verificação.
            try { File.Delete(tmp); } catch (Exception ex) { Debug.WriteLine($"Não removeu '{tmp}': {ex.Message}"); }
            throw;
        }

        Process.Start(new ProcessStartInfo { FileName = tmp, UseShellExecute = true });
    }

    /// <summary>
    /// Compara o SHA-256 do arquivo baixado com o checksum publicado. Sem
    /// <paramref name="checksumUrl"/> não há o que conferir e o método retorna
    /// em silêncio — releases anteriores à automação não publicam o .sha256.
    /// </summary>
    private static async Task VerificarIntegridadeAsync(
        HttpClient http, string arquivo, string? checksumUrl, CancellationToken ct)
    {
        if (checksumUrl is null) return;

        var publicado = ExtrairHash(await http.GetStringAsync(checksumUrl, ct).ConfigureAwait(false));
        if (publicado is null)
            throw new InvalidOperationException(
                "O arquivo de checksum do release está em formato inesperado. Atualização cancelada.");

        string calculado;
        using (var f = File.OpenRead(arquivo))
            calculado = Convert.ToHexString(await SHA256.HashDataAsync(f, ct).ConfigureAwait(false));

        if (!calculado.Equals(publicado, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "O instalador baixado não corresponde ao checksum publicado no release. " +
                "O arquivo pode estar corrompido ou ter sido adulterado; a atualização foi cancelada.");
    }

    /// <summary>
    /// Lê o hash de um arquivo no formato do utilitário sha256sum — "&lt;hash&gt;
    /// &lt;nome&gt;" — ou de um arquivo contendo apenas o hash. Devolve nulo se
    /// o conteúdo não parecer um SHA-256.
    /// </summary>
    internal static string? ExtrairHash(string? conteudo)
    {
        var primeiroCampo = conteudo?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (primeiroCampo is null || primeiroCampo.Length != 64) return null;

        return primeiroCampo.All(Uri.IsHexDigit) ? primeiroCampo : null;
    }
}
