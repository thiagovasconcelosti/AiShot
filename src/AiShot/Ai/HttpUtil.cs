namespace AiShot;

/// <summary>Utilitários compartilhados das chamadas HTTP.</summary>
internal static class HttpUtil
{
    /// <summary>
    /// Trunca o corpo de erro antes de expor em exceção/UI — evita despejar
    /// respostas longas (que podem conter eco do request/dados sensíveis).
    /// </summary>
    public static string Truncate(string? body, int max = 400)
    {
        if (string.IsNullOrEmpty(body)) return "";
        body = body.Trim();
        return body.Length <= max ? body : body[..max] + "…";
    }
}
