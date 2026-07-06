using System.Security.Cryptography;
using System.Text;

namespace AiShot.Config;

/// <summary>
/// Cifra/decifra segredos (API keys) em repouso usando DPAPI no escopo do
/// usuário atual (DataProtectionScope.CurrentUser). O conteúdo cifrado só pode
/// ser lido pelo mesmo usuário do Windows — um arquivo copiado para outra
/// máquina/usuário fica inútil. Tolerante a valores já em texto puro
/// (configs antigas migram para cifrado no próximo Save).
/// </summary>
internal static class SecretProtector
{
    private const string Prefix = "enc:";
    // Entropia adicional fixa (não é segredo — só separa o escopo do app).
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AiShot.v1");

    public static bool IsProtected(string? value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Cifra texto puro -> "enc:"+base64. Vazio/cifrado passa sem mudança.</summary>
    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain) || IsProtected(plain)) return plain ?? "";
        try
        {
            var data = Encoding.UTF8.GetBytes(plain);
            var enc = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(enc);
        }
        catch (Exception ex)
        {
            // Nunca degrada para texto puro em silêncio: persistir a API key em claro
            // seria o pior desfecho. Propaga para o chamador tratar/avisar o usuário.
            throw new InvalidOperationException(
                "Falha ao cifrar segredo (DPAPI). A chave NÃO foi salva em texto puro.", ex);
        }
    }

    /// <summary>Decifra "enc:"+base64 -> texto puro. Valor sem prefixo retorna como está.</summary>
    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !IsProtected(stored)) return stored ?? "";
        try
        {
            var enc = Convert.FromBase64String(stored[Prefix.Length..]);
            var data = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            return ""; // não foi possível decifrar (outro usuário/máquina) -> trata como vazio
        }
    }
}
