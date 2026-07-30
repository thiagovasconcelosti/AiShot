using System.Net.Http;
using System.Reflection;
using AiShot;

namespace AiShot.Tests;

/// <summary>
/// Configuração do cliente HTTP compartilhado do aplicativo.
/// </summary>
public class HttpClientFactoryTests
{
    [Fact]
    public void Create_NaoDefineTempoLimiteGlobal()
    {
        using var http = HttpClientFactory.Create();

        // Cada operação define o próprio limite via HttpUtil.Timeout: um limite
        // global cortaria o download do instalador, que é bem mais longo que uma
        // consulta à API.
        Assert.Equal(System.Threading.Timeout.InfiniteTimeSpan, http.Timeout);
    }

    [Fact]
    public void Create_ReciclaAsConexoesAgrupadas()
    {
        using var http = HttpClientFactory.Create();

        var handler = ObterHandler(http);

        var sockets = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.True(sockets.PooledConnectionLifetime < TimeSpan.MaxValue,
            "Sem reciclagem, o endereço resolvido na primeira chamada valeria para sempre.");
        Assert.InRange(sockets.PooledConnectionLifetime, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Create_DevolveInstanciasIndependentes()
    {
        using var a = HttpClientFactory.Create();
        using var b = HttpClientFactory.Create();

        Assert.NotSame(a, b);
    }

    /// <summary>
    /// Lê o handler interno do cliente. O <see cref="HttpClient"/> não o expõe,
    /// e a configuração de agrupamento de conexões vive nele.
    /// </summary>
    private static object ObterHandler(HttpClient http)
    {
        var campo = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "O campo do handler mudou de nome — o teste precisa acompanhar a implementação.");

        return campo.GetValue(http)
            ?? throw new InvalidOperationException("O cliente foi criado sem handler.");
    }
}
