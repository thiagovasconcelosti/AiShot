using System.Net.Http;

namespace AiShot;

/// <summary>
/// Cria o <see cref="HttpClient"/> usado pelo aplicativo.
/// </summary>
internal static class HttpClientFactory
{
    /// <summary>
    /// Tempo de vida das conexões agrupadas. O AiShot fica na bandeja por dias:
    /// sem reciclagem, as conexões guardam para sempre o endereço resolvido na
    /// primeira chamada, e uma troca de DNS do provedor (failover, mudança de
    /// balanceador) só teria efeito depois de reiniciar o aplicativo.
    /// </summary>
    private static readonly TimeSpan TempoDeVidaDaConexao = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Cliente sem tempo limite global: cada operação define o seu via
    /// <see cref="HttpUtil.Timeout"/>, porque os limites variam bastante entre
    /// consultar a versão mais recente e baixar um instalador inteiro.
    /// </summary>
    public static HttpClient Create() =>
        new(new SocketsHttpHandler { PooledConnectionLifetime = TempoDeVidaDaConexao })
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
}
