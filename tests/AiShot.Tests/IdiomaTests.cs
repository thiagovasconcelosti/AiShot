using System.Globalization;
using AiShot.Resources;

namespace AiShot.Tests;

/// <summary>
/// Seleção do idioma da interface e cobertura das traduções.
/// </summary>
public sealed class IdiomaTests : IDisposable
{
    private readonly CultureInfo _culturaOriginal = CultureInfo.CurrentUICulture;

    public void Dispose()
    {
        // A cultura é estado de thread; deixá-la trocada contaminaria os testes
        // que rodarem em seguida na mesma thread.
        Idioma.Aplicar(Idioma.Automatico);
        CultureInfo.CurrentUICulture = _culturaOriginal;
        GC.SuppressFinalize(this);
    }

    // ---------- Resolução da etiqueta ----------

    [Theory]
    [InlineData("pt")]
    [InlineData("en")]
    [InlineData("es")]
    public void Resolver_ComIdiomaConhecido_DevolveACultura(string tag) =>
        Assert.Equal(tag, Idioma.Resolver(tag)?.TwoLetterISOLanguageName);

    [Theory]
    [InlineData("PT")]
    [InlineData("En")]
    public void Resolver_IgnoraDiferencaDeCaixa(string tag) =>
        Assert.NotNull(Idioma.Resolver(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public void Resolver_ComAutomatico_DevolveNulo(string? tag) =>
        Assert.Null(Idioma.Resolver(tag));

    [Theory]
    [InlineData("ja")]
    [InlineData("de-CH")]
    [InlineData("xx-XX")]
    [InlineData("nao-e-cultura")]
    public void Resolver_ComIdiomaDesconhecido_DevolveNulo(string tag)
    {
        // Uma etiqueta arbitrária (config editada à mão, ou de versão futura)
        // não pode virar exceção na inicialização — cai no idioma do sistema.
        Assert.Null(Idioma.Resolver(tag));
    }

    // ---------- Aplicação ----------

    [Fact]
    public void Aplicar_ComIdiomaFixo_TrocaACulturaCorrente()
    {
        Idioma.Aplicar("en");

        Assert.Equal("en", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public void Aplicar_ComAutomatico_VoltaAoIdiomaDoSistema()
    {
        Idioma.Aplicar("es");

        Idioma.Aplicar(Idioma.Automatico);

        Assert.Equal(
            CultureInfo.InstalledUICulture.TwoLetterISOLanguageName,
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public void Aplicar_ComEtiquetaInvalida_NaoLanca()
    {
        Idioma.Aplicar("nao-existe");

        Assert.True(true); // chegar aqui já é o resultado esperado
    }

    // ---------- Traduções ----------

    [Theory]
    [InlineData("pt")]
    [InlineData("en")]
    [InlineData("es")]
    public void TodasAsChaves_TemTraducaoNoIdioma(string tag)
    {
        Idioma.Aplicar(tag);

        var faltando = ChavesDeInterface()
            .Where(c => string.IsNullOrWhiteSpace(Buscar(c)))
            .ToArray();

        Assert.True(faltando.Length == 0,
            $"Sem tradução em '{tag}': {string.Join(", ", faltando)}");
    }

    [Fact]
    public void Traducoes_DiferemEntreOsIdiomas()
    {
        // Se o satélite não fosse carregado, todos os idiomas devolveriam o
        // texto neutro e o teste acima passaria sem haver tradução alguma.
        Idioma.Aplicar("pt");
        var portugues = Buscar("ActionCopy");

        Idioma.Aplicar("en");
        var ingles = Buscar("ActionCopy");

        Assert.NotEqual(portugues, ingles);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void MensagensComMarcador_PreservamOMarcadorNaTraducao(string tag)
    {
        // Uma tradução que perde o {0} engoliria o detalhe do erro — a mensagem
        // apareceria sem a causa.
        Idioma.Aplicar(tag);

        foreach (var chave in ChavesComMarcador())
        {
            var texto = Buscar(chave);
            Assert.True(texto!.Contains("{0}", StringComparison.Ordinal),
                $"'{chave}' em '{tag}' perdeu o marcador {{0}}: \"{texto}\"");
        }
    }

    [Fact]
    public void Disponiveis_ComecaPeloAutomatico()
    {
        // É o padrão, e o primeiro item da lista é o que aparece selecionado
        // antes de o usuário escolher.
        Assert.Equal(Idioma.Automatico, Idioma.Disponiveis[0].Tag);
    }

    [Fact]
    public void Disponiveis_NaoRepeteEtiquetas() =>
        Assert.Equal(
            Idioma.Disponiveis.Count,
            Idioma.Disponiveis.Select(d => d.Tag).Distinct(StringComparer.OrdinalIgnoreCase).Count());

    [Fact]
    public void Disponiveis_TodasAsEtiquetasResolvem()
    {
        foreach (var (tag, _) in Idioma.Disponiveis.Where(d => d.Tag != Idioma.Automatico))
            Assert.NotNull(Idioma.Resolver(tag));
    }

    // ---------- Apoio ----------

    /// <summary>Lê uma chave pelo ResourceManager, respeitando a cultura atual.</summary>
    private static string? Buscar(string chave) =>
        Strings.ResourceManager.GetString(chave, CultureInfo.CurrentUICulture);

    /// <summary>
    /// Chaves declaradas na classe gerada a partir do .resx neutro. Vem por
    /// reflexão de propósito: uma chave nova aparece aqui sozinha, sem que a
    /// lista precise ser mantida à mão.
    /// </summary>
    private static IEnumerable<string> ChavesDeInterface() =>
        typeof(Strings)
            .GetProperties(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .Where(n => n != "ResourceManager" && n != "Culture");

    /// <summary>Chaves cujo texto em português traz {0}.</summary>
    private static IEnumerable<string> ChavesComMarcador()
    {
        var anterior = CultureInfo.CurrentUICulture;
        try
        {
            Idioma.Aplicar("pt");
            return ChavesDeInterface()
                .Where(c => Buscar(c)?.Contains("{0}", StringComparison.Ordinal) == true)
                .ToArray();
        }
        finally
        {
            CultureInfo.CurrentUICulture = anterior;
        }
    }
}
