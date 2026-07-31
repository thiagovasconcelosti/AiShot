using System.Reflection;
using System.Xml.Linq;

namespace AiShot.Tests;

/// <summary>
/// Versão do projeto: a fonte única em Directory.Build.props e o que chega ao
/// assembly compilado.
/// </summary>
/// <remarks>
/// O fluxo de release confere a versão do arquivo contra a tag antes de
/// publicar. Essa conferência já falhou por ler o XML de um jeito que quebra
/// quando há mais de um &lt;PropertyGroup&gt; — os testes aqui fixam o formato
/// que o fluxo espera encontrar.
/// </remarks>
public class VersaoDoProjetoTests
{
    /// <summary>Sobe a partir da saída de teste até achar Directory.Build.props.</summary>
    private static string CaminhoDoArquivo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidato = Path.Combine(dir.FullName, "Directory.Build.props");
            if (File.Exists(candidato)) return candidato;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Directory.Build.props não encontrado a partir da saída de teste.");
    }

    private static XDocument Documento() => XDocument.Load(CaminhoDoArquivo());

    [Fact]
    public void Arquivo_DeclaraExatamenteUmaVersao()
    {
        // Duas declarações fariam o fluxo de release escolher uma delas sem
        // critério, e o instalador poderia sair com versão diferente do assembly.
        var nos = Documento().Descendants("AiShotVersion").ToArray();

        Assert.Single(nos);
    }

    [Fact]
    public void Versao_EstaNoFormatoQueOFluxoEspera()
    {
        // A tag é "v" + este valor. Um espaço ou sufixo aqui viraria uma tag que
        // não corresponde ao arquivo, e o fluxo aborta.
        var texto = Documento().Descendants("AiShotVersion").Single().Value;

        Assert.Equal(texto.Trim(), texto);
        Assert.Matches(@"^\d+\.\d+\.\d+$", texto);
    }

    [Fact]
    public void Versao_ChegaAoAssembly()
    {
        // Se a propriedade parasse de alimentar o csproj, o instalador teria a
        // versão do arquivo e o executável outra — o auto-update entraria em
        // laço de reinstalação.
        var doArquivo = Documento().Descendants("AiShotVersion").Single().Value.Trim();

        var doAssembly = typeof(AiShot.Config.AppConfig).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        // O atributo traz "0.1.4+<sha>" quando o repositório tem histórico.
        Assert.StartsWith(doArquivo, doAssembly, StringComparison.Ordinal);
    }

    [Fact]
    public void Arquivo_PodeSerLidoComOMesmoXPathDoFluxo()
    {
        // Reproduz a consulta usada em release.yml. O acesso por propriedade
        // ($props.Project.PropertyGroup.AiShotVersion) devolvia nulo quando o
        // arquivo passou a ter dois <PropertyGroup>; o XPath não depende disso.
        var doc = new System.Xml.XmlDocument();
        doc.Load(CaminhoDoArquivo());

        var no = doc.SelectSingleNode("/Project/PropertyGroup/AiShotVersion");

        Assert.NotNull(no);
        Assert.False(string.IsNullOrWhiteSpace(no!.InnerText));
    }
}
