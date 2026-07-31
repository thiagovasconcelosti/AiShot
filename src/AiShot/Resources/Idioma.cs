using System.Globalization;

namespace AiShot.Resources;

/// <summary>
/// Seleção do idioma da interface.
/// </summary>
/// <remarks>
/// O padrão segue a cultura do sistema; a configuração permite fixar um idioma.
/// Idiomas para os quais não há tradução caem no português, que é o idioma-fonte
/// do projeto (o <c>.resx</c> neutro).
/// </remarks>
public static class Idioma
{
    /// <summary>Valor de configuração que significa "seguir o sistema".</summary>
    public const string Automatico = "auto";

    /// <summary>
    /// Idiomas com tradução completa. A ordem é a que aparece nas Configurações.
    /// </summary>
    public static IReadOnlyList<(string Tag, string Nome)> Disponiveis { get; } =
    [
        (Automatico, "Automático (sistema)"),
        ("pt", "Português"),
        ("en", "English"),
        ("es", "Español"),
    ];

    /// <summary>
    /// Aplica o idioma às threads do aplicativo. Chamado na inicialização e
    /// sempre que a configuração muda.
    /// </summary>
    /// <param name="tag">
    /// Etiqueta de cultura (<c>pt</c>, <c>en</c>, <c>es</c>) ou
    /// <see cref="Automatico"/> para seguir o sistema.
    /// </param>
    public static void Aplicar(string? tag)
    {
        var cultura = Resolver(tag);

        // DefaultThreadCurrentUICulture cobre as threads criadas depois desta
        // chamada; a corrente precisa ser ajustada à parte, senão a janela que
        // já está aberta continuaria no idioma anterior.
        CultureInfo.DefaultThreadCurrentUICulture = cultura;
        CultureInfo.CurrentUICulture = cultura ?? CultureInfo.InstalledUICulture;

        // A classe gerada consulta esta propriedade antes da cultura da thread;
        // deixá-la nula faz o ResourceManager usar a cultura corrente.
        Strings.Culture = cultura;
    }

    /// <summary>
    /// Converte a etiqueta configurada em cultura. Devolve null para
    /// "automático" e para etiquetas que não reconhecemos — em ambos os casos
    /// vale a cultura do sistema.
    /// </summary>
    internal static CultureInfo? Resolver(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        tag = tag.Trim();
        if (string.Equals(tag, Automatico, StringComparison.OrdinalIgnoreCase)) return null;

        // Só aceitamos o que sabemos traduzir. Uma etiqueta arbitrária vinda da
        // configuração (editada à mão, ou de uma versão futura) não pode virar
        // exceção na inicialização.
        bool conhecido = Disponiveis.Any(d =>
            d.Tag != Automatico && string.Equals(d.Tag, tag, StringComparison.OrdinalIgnoreCase));
        if (!conhecido) return null;

        try { return CultureInfo.GetCultureInfo(tag); }
        catch (CultureNotFoundException) { return null; }
    }
}
