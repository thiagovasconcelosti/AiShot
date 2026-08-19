using System.Runtime.InteropServices;
using System.Text;

namespace AiShot.App;

/// <summary>
/// Identifica o canal pela identidade atribuida pelo Windows ao processo.
/// Instalacoes Inno/portable nao possuem identidade; MSIX, inclusive o pacote
/// de desenvolvimento, possui. Nao depende de caminho, argumento ou arquivo
/// que o usuario possa copiar entre instalacoes.
/// </summary>
public static class DistributionChannel
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    private static readonly Lazy<bool> Packaged = new(DetectPackageIdentity);

    public static bool IsStorePackage => Packaged.Value;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(
        ref int packageFullNameLength,
        StringBuilder? packageFullName);

    private static bool DetectPackageIdentity()
    {
        var length = 0;
        var result = GetCurrentPackageFullName(ref length, null);
        return ResultHasPackageIdentity(result);
    }

    /// <summary>
    /// A primeira consulta sem buffer retorna ERROR_INSUFFICIENT_BUFFER quando
    /// existe uma identidade. ERROR_SUCCESS tambem e aceito por seguranca para
    /// eventuais mudancas de implementacao da API.
    /// </summary>
    internal static bool ResultHasPackageIdentity(int result) =>
        result is ErrorSuccess or ErrorInsufficientBuffer;
}
