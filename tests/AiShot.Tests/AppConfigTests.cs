using System.Text.Json;
using AiShot.Config;

namespace AiShot.Tests;

/// <summary>
/// Carga, gravação e overrides de ambiente da configuração.
/// Cada teste usa um arquivo próprio em pasta temporária.
/// </summary>
/// <remarks>
/// A classe roda em série (<see cref="CollectionAttribute"/>) porque variáveis de
/// ambiente são estado global do processo: com execução paralela, o override de
/// um teste vaza para o <c>Load</c> de outro e a falha aparece longe da causa.
/// </remarks>
[Collection("ambiente")]
public class AppConfigTests : IDisposable
{
    private readonly string _pasta;
    private readonly string _caminho;

    public AppConfigTests()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "AiShot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pasta);
        _caminho = Path.Combine(_pasta, "appsettings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_pasta, recursive: true); } catch { /* melhor esforço */ }
        GC.SuppressFinalize(this);
    }

    // ---------- Padrões ----------

    [Fact]
    public void Load_SemArquivo_DevolveOsPadroes()
    {
        var cfg = AppConfig.Load(_caminho);

        Assert.Equal("PrintScreen", cfg.HotKey);
        Assert.False(cfg.CloseOnCopy);
        Assert.Equal("anthropic", cfg.Ai.Provider);
        Assert.Equal("freeimage", cfg.ImageUpload.Service);
    }

    [Fact]
    public void Load_ComCaminhoExplicitoInexistente_NaoLeAConfiguracaoLegada()
    {
        // A migração legada lê o appsettings.json ao lado do executável. Ela vale
        // apenas para o caminho padrão: com um caminho explícito, o chamador pediu
        // um arquivo específico e ler outro no lugar seria uma surpresa.
        //
        // Cobre uma regressão real: a pasta de saída dos testes pode receber uma
        // cópia do appsettings.json do projeto principal, que era carregada no
        // lugar dos padrões e fazia Load_SemArquivo_DevolveOsPadroes falhar.
        //
        // O arquivo legado é criado aqui em vez de assumido: numa árvore recém
        // clonada ele não existe, e depender do resíduo de um build anterior
        // tornaria o teste verde por acidente numa máquina e vermelho na outra.
        using var legado = new ArquivoLegadoTemporario("""{"ai":{"provider":"openai"}}""");

        var cfg = AppConfig.Load(_caminho);

        Assert.Equal(new AppConfig().Ai.Provider, cfg.Ai.Provider);
        Assert.False(File.Exists(_caminho)); // e não regravou nada no caminho pedido
    }

    /// <summary>
    /// Coloca um appsettings.json ao lado do assembly em execução — o local que
    /// <c>AppConfig</c> trata como configuração legada — e restaura o estado
    /// anterior ao ser descartado, inclusive quando já havia um arquivo lá.
    /// </summary>
    private sealed class ArquivoLegadoTemporario : IDisposable
    {
        private readonly string _caminho = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        private readonly string? _conteudoAnterior;

        public ArquivoLegadoTemporario(string conteudo)
        {
            _conteudoAnterior = File.Exists(_caminho) ? File.ReadAllText(_caminho) : null;
            File.WriteAllText(_caminho, conteudo);
        }

        public void Dispose()
        {
            if (_conteudoAnterior is null) File.Delete(_caminho);
            else File.WriteAllText(_caminho, _conteudoAnterior);
        }
    }

    // ---------- Ida e volta ----------

    [Fact]
    public void Save_SeguidoDeLoad_PreservaOsValores()
    {
        var original = AppConfig.Load(_caminho);
        original.HotKey = "Ctrl+Alt+S";
        original.CloseOnCopy = true;
        original.Ai.Provider = "openai";
        original.Ai.Model = "modelo-de-teste";
        original.Ai.BaseUrl = "https://exemplo.invalido";
        original.ImageUpload.Service = "imgbb";
        original.Save(_caminho);

        var lido = AppConfig.Load(_caminho);

        Assert.Equal("Ctrl+Alt+S", lido.HotKey);
        Assert.True(lido.CloseOnCopy);
        Assert.Equal("openai", lido.Ai.Provider);
        Assert.Equal("modelo-de-teste", lido.Ai.Model);
        Assert.Equal("https://exemplo.invalido", lido.Ai.BaseUrl);
        Assert.Equal("imgbb", lido.ImageUpload.Service);
    }

    [Fact]
    public void Save_CifraAsChavesNoArquivoEAsDevolveEmClaroAoCarregar()
    {
        const string chave = "sk-ant-api03-chave-de-teste";
        var cfg = AppConfig.Load(_caminho);
        cfg.Ai.ApiKey = chave;
        cfg.Ai.Vision.ApiKey = chave;
        cfg.Ai.Fallback!.ApiKey = chave;
        cfg.ImageUpload.ApiKey = chave;
        cfg.Save(_caminho);

        // No disco: nenhuma ocorrência do texto em claro.
        var json = File.ReadAllText(_caminho);
        Assert.DoesNotContain(chave, json, StringComparison.Ordinal);

        // Em memória, após carregar: de volta em claro, pronto para uso.
        var lido = AppConfig.Load(_caminho);
        Assert.Equal(chave, lido.Ai.ApiKey);
        Assert.Equal(chave, lido.Ai.Vision.ApiKey);
        Assert.Equal(chave, lido.Ai.Fallback!.ApiKey);
        Assert.Equal(chave, lido.ImageUpload.ApiKey);
    }

    [Fact]
    public void Save_MantemAInstanciaEmMemoriaComAsChavesEmClaro()
    {
        const string chave = "sk-ant-api03-em-memoria";
        var cfg = AppConfig.Load(_caminho);
        cfg.Ai.ApiKey = chave;

        cfg.Save(_caminho);

        // Gravar não pode cifrar a instância viva — ela segue em uso pelo app.
        Assert.Equal(chave, cfg.Ai.ApiKey);
    }

    [Fact]
    public void Save_ChamadoDuasVezes_NaoCifraDuplamente()
    {
        const string chave = "sk-ant-api03-duas-vezes";
        var cfg = AppConfig.Load(_caminho);
        cfg.Ai.ApiKey = chave;

        cfg.Save(_caminho);
        cfg.Save(_caminho);

        Assert.Equal(chave, AppConfig.Load(_caminho).Ai.ApiKey);
    }

    // ---------- Gravação atômica ----------

    [Fact]
    public void Save_SobreArquivoExistente_NaoDeixaTemporarioParaTras()
    {
        var cfg = AppConfig.Load(_caminho);
        cfg.Save(_caminho);
        cfg.HotKey = "F12";
        cfg.Save(_caminho);

        Assert.False(File.Exists(_caminho + ".tmp"));
        Assert.Equal("F12", AppConfig.Load(_caminho).HotKey);
    }

    [Fact]
    public void Save_CriaODiretorioQuandoEleNaoExiste()
    {
        var aninhado = Path.Combine(_pasta, "a", "b", "appsettings.json");

        AppConfig.Load(aninhado).Save(aninhado);

        Assert.True(File.Exists(aninhado));
    }

    // ---------- Arquivo inválido ----------

    [Fact]
    public void Load_ComJsonVazio_DevolveOsPadroes()
    {
        File.WriteAllText(_caminho, "null");

        Assert.Equal("PrintScreen", AppConfig.Load(_caminho).HotKey);
    }

    [Fact]
    public void Load_ComCamposDesconhecidos_IgnoraOsExtras()
    {
        File.WriteAllText(_caminho, """{"hotKey":"F9","campoInexistente":123}""");

        Assert.Equal("F9", AppConfig.Load(_caminho).HotKey);
    }

    [Fact]
    public void Load_ComNomesEmCaixaDiferente_AindaAssimReconhece()
    {
        File.WriteAllText(_caminho, """{"HOTKEY":"F8"}""");

        Assert.Equal("F8", AppConfig.Load(_caminho).HotKey);
    }

    // ---------- Overrides de ambiente ----------

    [Fact]
    public void ApplyEnvironmentOverrides_SobrescreveOsValoresDoArquivo()
    {
        using var env = new VariaveisDeAmbiente(
            ("AISHOT_HOTKEY", "Ctrl+F1"),
            ("AISHOT_CLOSEONCOPY", "true"),
            ("AISHOT_AI__PROVIDER", "openai"),
            ("AISHOT_AI__APIKEY", "chave-do-ambiente"),
            ("AISHOT_AI__MODEL", "modelo-do-ambiente"),
            ("AISHOT_AI__BASEURL", "https://base.invalido"),
            ("AISHOT_IMAGEUPLOAD__SERVICE", "imgbb"),
            ("AISHOT_IMAGEUPLOAD__APIKEY", "chave-de-upload"));

        var cfg = new AppConfig();
        cfg.ApplyEnvironmentOverrides();

        Assert.Equal("Ctrl+F1", cfg.HotKey);
        Assert.True(cfg.CloseOnCopy);
        Assert.Equal("openai", cfg.Ai.Provider);
        Assert.Equal("chave-do-ambiente", cfg.Ai.ApiKey);
        Assert.Equal("modelo-do-ambiente", cfg.Ai.Model);
        Assert.Equal("https://base.invalido", cfg.Ai.BaseUrl);
        Assert.Equal("imgbb", cfg.ImageUpload.Service);
        Assert.Equal("chave-de-upload", cfg.ImageUpload.ApiKey);
    }

    [Fact]
    public void ApplyEnvironmentOverrides_CobreOEndpointDeFallback()
    {
        using var env = new VariaveisDeAmbiente(
            ("AISHOT_AI__FALLBACK__PROVIDER", "anthropic"),
            ("AISHOT_AI__FALLBACK__APIKEY", "chave-de-fallback"),
            ("AISHOT_AI__FALLBACK__MODEL", "modelo-de-fallback"),
            ("AISHOT_AI__FALLBACK__BASEURL", "https://fallback.invalido"));

        var cfg = new AppConfig();
        cfg.ApplyEnvironmentOverrides();

        Assert.Equal("anthropic", cfg.Ai.Fallback!.Provider);
        Assert.Equal("chave-de-fallback", cfg.Ai.Fallback.ApiKey);
        Assert.Equal("modelo-de-fallback", cfg.Ai.Fallback.Model);
        Assert.Equal("https://fallback.invalido", cfg.Ai.Fallback.BaseUrl);
    }

    [Fact]
    public void ApplyEnvironmentOverrides_CobreOEndpointDeVisao()
    {
        using var env = new VariaveisDeAmbiente(
            ("AISHOT_AI__VISION__ENABLED", "true"),
            ("AISHOT_AI__VISION__PROVIDER", "openai"),
            ("AISHOT_AI__VISION__APIKEY", "chave-de-visao"),
            ("AISHOT_AI__VISION__MODEL", "modelo-de-visao"),
            ("AISHOT_AI__VISION__BASEURL", "https://visao.invalido"));

        var cfg = new AppConfig();
        cfg.ApplyEnvironmentOverrides();

        Assert.True(cfg.Ai.Vision.Enabled);
        Assert.Equal("openai", cfg.Ai.Vision.Provider);
        Assert.Equal("chave-de-visao", cfg.Ai.Vision.ApiKey);
        Assert.Equal("modelo-de-visao", cfg.Ai.Vision.Model);
        Assert.Equal("https://visao.invalido", cfg.Ai.Vision.BaseUrl);
    }

    [Fact]
    public void ApplyEnvironmentOverrides_SemVariaveis_NaoAlteraNada()
    {
        var cfg = new AppConfig { HotKey = "F5" };
        cfg.Ai.Model = "intocado";

        cfg.ApplyEnvironmentOverrides();

        Assert.Equal("F5", cfg.HotKey);
        Assert.Equal("intocado", cfg.Ai.Model);
    }

    [Fact]
    public void ApplyEnvironmentOverrides_ComBooleanoInvalido_PreservaOValorAtual()
    {
        using var env = new VariaveisDeAmbiente(("AISHOT_CLOSEONCOPY", "talvez"));

        var cfg = new AppConfig { CloseOnCopy = true };
        cfg.ApplyEnvironmentOverrides();

        Assert.True(cfg.CloseOnCopy);
    }

    [Fact]
    public void Load_AplicaOsOverridesDepoisDeLerOArquivo()
    {
        File.WriteAllText(_caminho, """{"hotKey":"F1"}""");
        using var env = new VariaveisDeAmbiente(("AISHOT_HOTKEY", "F2"));

        // O ambiente tem precedência sobre o arquivo.
        Assert.Equal("F2", AppConfig.Load(_caminho).HotKey);
    }

    /// <summary>Define variáveis de ambiente e as restaura ao ser descartada.</summary>
    private sealed class VariaveisDeAmbiente : IDisposable
    {
        private readonly (string Nome, string? Anterior)[] _anteriores;

        public VariaveisDeAmbiente(params (string Nome, string Valor)[] variaveis)
        {
            _anteriores = variaveis
                .Select(v => (v.Nome, Environment.GetEnvironmentVariable(v.Nome)))
                .ToArray();

            foreach (var (nome, valor) in variaveis)
                Environment.SetEnvironmentVariable(nome, valor);
        }

        public void Dispose()
        {
            foreach (var (nome, anterior) in _anteriores)
                Environment.SetEnvironmentVariable(nome, anterior);
        }
    }
}
