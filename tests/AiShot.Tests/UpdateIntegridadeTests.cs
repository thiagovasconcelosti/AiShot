using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AiShot.App;

namespace AiShot.Tests;

/// <summary>
/// Verificação de integridade do instalador baixado pelo auto-update.
/// </summary>
/// <remarks>
/// O método verificado é privado de propósito — não faz parte do contrato do
/// serviço. É alcançado por reflexão porque é a única parte da issue #10 que
/// decide entre executar e abortar um binário, e testá-lo indiretamente exigiria
/// um servidor HTTP num host do GitHub.
/// </remarks>
public class UpdateIntegridadeTests : IDisposable
{
    private readonly string _pasta;

    public UpdateIntegridadeTests()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "AiShot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pasta);
    }

    public void Dispose()
    {
        try { Directory.Delete(_pasta, recursive: true); } catch { /* melhor esforço */ }
        GC.SuppressFinalize(this);
    }

    private static readonly MethodInfo Verificar =
        typeof(UpdateService).GetMethod("VerificarIntegridadeAsync", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "VerificarIntegridadeAsync não encontrado — o teste precisa acompanhar o nome do método.");

    /// <summary>Invoca o método privado, desembrulhando a exceção da reflexão.</summary>
    private static async Task InvocarAsync(HttpClient http, string arquivo, string? checksumUrl)
    {
        var tarefa = (Task)Verificar.Invoke(null, [http, arquivo, checksumUrl, CancellationToken.None])!;
        try { await tarefa; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { throw ex.InnerException; }
    }

    private string CriarArquivo(string conteudo)
    {
        var caminho = Path.Combine(_pasta, "instalador.exe");
        File.WriteAllText(caminho, conteudo);
        return caminho;
    }

    private static string Sha256De(string conteudo) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)));

    /// <summary>Serve um corpo fixo, sem rede — substitui o download do .sha256.</summary>
    private static HttpClient ClienteQueDevolve(string corpo) =>
        new(new RespostaFixa(corpo)) { BaseAddress = new Uri("https://github.com/") };

    private sealed class RespostaFixa(string corpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(corpo),
            });
    }

    // ---------- Arquivo íntegro ----------

    [Fact]
    public async Task ComHashCorrespondente_NaoLanca()
    {
        const string conteudo = "instalador legítimo";
        var arquivo = CriarArquivo(conteudo);
        using var http = ClienteQueDevolve($"{Sha256De(conteudo)}  instalador.exe");

        var excecao = await Record.ExceptionAsync(
            () => InvocarAsync(http, arquivo, "https://github.com/x/y.sha256"));

        Assert.Null(excecao);
    }

    [Fact]
    public async Task ComHashEmMinusculas_NaoLanca()
    {
        const string conteudo = "instalador legítimo";
        var arquivo = CriarArquivo(conteudo);
        using var http = ClienteQueDevolve(Sha256De(conteudo).ToLowerInvariant());

        var excecao = await Record.ExceptionAsync(
            () => InvocarAsync(http, arquivo, "https://github.com/x/y.sha256"));

        Assert.Null(excecao);
    }

    // ---------- Arquivo adulterado ----------

    [Fact]
    public async Task ComHashDivergente_Lanca()
    {
        // O arquivo no disco não é o que o release publicou.
        var arquivo = CriarArquivo("instalador ADULTERADO");
        using var http = ClienteQueDevolve($"{Sha256De("instalador legítimo")}  instalador.exe");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvocarAsync(http, arquivo, "https://github.com/x/y.sha256"));

        Assert.Contains("não corresponde", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ComUmUnicoByteAlterado_Lanca()
    {
        var arquivo = CriarArquivo("conteudo");
        using var http = ClienteQueDevolve(Sha256De("conteudA")); // difere por um caractere

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvocarAsync(http, arquivo, "https://github.com/x/y.sha256"));
    }

    // ---------- Arquivo de checksum inutilizável ----------

    [Theory]
    [InlineData("")]
    [InlineData("<!DOCTYPE html><html>404 Not Found</html>")]
    [InlineData("hash-que-nao-e-hash")]
    public async Task ComChecksumIlegivel_Lanca(string corpo)
    {
        // Um .sha256 corrompido ou uma página de erro no lugar dele não pode
        // resultar em "não deu para conferir, então executa assim mesmo".
        var arquivo = CriarArquivo("qualquer coisa");
        using var http = ClienteQueDevolve(corpo);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvocarAsync(http, arquivo, "https://github.com/x/y.sha256"));

        Assert.Contains("formato inesperado", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Release sem checksum publicado ----------

    [Fact]
    public async Task SemUrlDeChecksum_NaoLanca()
    {
        // Releases anteriores à automação não publicam o .sha256. Não há o que
        // conferir, e recusar a atualização deixaria esses usuários travados.
        var arquivo = CriarArquivo("instalador antigo");
        using var http = ClienteQueDevolve("irrelevante");

        var excecao = await Record.ExceptionAsync(() => InvocarAsync(http, arquivo, null));

        Assert.Null(excecao);
    }
}
