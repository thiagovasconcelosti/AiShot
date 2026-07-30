using AiShot.History;

namespace AiShot.Tests;

/// <summary>
/// Retenção do histórico de capturas: limite de itens, limite de espaço e
/// ordenação.
/// </summary>
public sealed class CaptureHistoryTests : IDisposable
{
    private readonly string _pasta;

    public CaptureHistoryTests()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "aishot-hist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pasta);
    }

    public void Dispose()
    {
        try { Directory.Delete(_pasta, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>Bytes de tamanho conhecido; o conteúdo não precisa ser um PNG válido.</summary>
    private static byte[] Bytes(int quantidade) => new byte[quantidade];

    private CaptureHistory Criar(int maxItens = 10, int maxMb = 100) =>
        new(_pasta, maxItens, maxMb);

    // Momentos distintos e crescentes, para que a ordenação seja determinística.
    private static DateTime Momento(int i) => new DateTime(2026, 1, 1, 12, 0, 0).AddSeconds(i);

    // ---------- Gravação ----------

    [Fact]
    public void Adicionar_GravaOArquivo()
    {
        var caminho = Criar().Adicionar(Bytes(10), Momento(0));

        Assert.True(File.Exists(caminho));
        Assert.Equal(10, new FileInfo(caminho).Length);
    }

    [Fact]
    public void Adicionar_CriaAPastaQuandoNaoExiste()
    {
        var subpasta = Path.Combine(_pasta, "ainda-nao-existe");
        var historico = new CaptureHistory(subpasta, 10, 100);

        historico.Adicionar(Bytes(10), Momento(0));

        Assert.True(Directory.Exists(subpasta));
    }

    [Fact]
    public void Adicionar_ComDuasCapturasNoMesmoInstante_NaoSobrescreve()
    {
        // O carimbo de tempo tem resolução de milissegundo; duas capturas
        // rápidas cairiam no mesmo nome e uma apagaria a outra.
        var historico = Criar();
        var mesmo = Momento(0);

        var a = historico.Adicionar(Bytes(10), mesmo);
        var b = historico.Adicionar(Bytes(20), mesmo);

        Assert.NotEqual(a, b);
        Assert.Equal(2, historico.Listar().Count);
    }

    // ---------- Listagem ----------

    [Fact]
    public void Listar_ComPastaInexistente_DevolveVazio()
    {
        var historico = new CaptureHistory(Path.Combine(_pasta, "nao-existe"), 10, 100);

        Assert.Empty(historico.Listar());
    }

    [Fact]
    public void Listar_DaMaisRecenteParaAMaisAntiga()
    {
        var historico = Criar();
        for (int i = 0; i < 3; i++)
        {
            var caminho = historico.Adicionar(Bytes(10), Momento(i));
            // A ordenação usa a data de modificação do arquivo, não o nome.
            File.SetLastWriteTimeUtc(caminho, Momento(i).ToUniversalTime());
        }

        var itens = historico.Listar();

        Assert.Equal(3, itens.Count);
        Assert.True(itens[0].Momento > itens[1].Momento);
        Assert.True(itens[1].Momento > itens[2].Momento);
    }

    [Fact]
    public void Listar_IgnoraArquivosDeOutraOrigem()
    {
        File.WriteAllText(Path.Combine(_pasta, "anotacoes.txt"), "não é captura");
        File.WriteAllBytes(Path.Combine(_pasta, "outro.png"), Bytes(10));

        var historico = Criar();
        historico.Adicionar(Bytes(10), Momento(0));

        Assert.Single(historico.Listar());
    }

    // ---------- Limite de itens ----------

    [Fact]
    public void Adicionar_AlemDoLimiteDeItens_DescartaAsMaisAntigas()
    {
        var historico = Criar(maxItens: 3);

        for (int i = 0; i < 6; i++)
        {
            var caminho = historico.Adicionar(Bytes(10), Momento(i));
            File.SetLastWriteTimeUtc(caminho, Momento(i).ToUniversalTime());
        }

        var itens = historico.Listar();

        Assert.Equal(3, itens.Count);
        // As que sobraram são as três últimas.
        Assert.All(itens, item => Assert.True(item.Momento >= Momento(3).ToUniversalTime()));
    }

    [Fact]
    public void Adicionar_ComLimiteDeItensZero_AindaGuardaUma()
    {
        // Zero desligaria o histórico por uma porta que não é a chave de
        // ligar/desligar; o mínimo coerente é uma captura.
        var historico = Criar(maxItens: 0);

        historico.Adicionar(Bytes(10), Momento(0));

        Assert.Single(historico.Listar());
    }

    // ---------- Limite de espaço ----------

    [Fact]
    public void Adicionar_AlemDoLimiteDeEspaco_DescartaAsMaisAntigas()
    {
        // 1 MB de teto, capturas de 400 KB: cabem duas.
        const int quatrocentosKb = 400 * 1024;
        var historico = Criar(maxItens: 100, maxMb: 1);

        for (int i = 0; i < 5; i++)
        {
            var caminho = historico.Adicionar(Bytes(quatrocentosKb), Momento(i));
            File.SetLastWriteTimeUtc(caminho, Momento(i).ToUniversalTime());
        }

        var itens = historico.Listar();

        Assert.Equal(2, itens.Count);
        Assert.True(itens.Sum(i => i.Bytes) <= 1024L * 1024L);
    }

    [Fact]
    public void Adicionar_ComCapturaMaiorQueOLimite_AindaGuardaAUltima()
    {
        // Apagar a captura recém-gravada esvaziaria o histórico na hora — pior
        // que estourar o teto por um item.
        var historico = Criar(maxItens: 10, maxMb: 1);

        historico.Adicionar(Bytes(2 * 1024 * 1024), Momento(0));

        Assert.Single(historico.Listar());
    }

    [Fact]
    public void Adicionar_OLimiteDeEspacoNaoContaOQueJaFoiDescartadoPelaContagem()
    {
        // Os dois limites atuam sobre o mesmo conjunto: o de espaço olha o que
        // sobrou depois do de contagem, não a pasta inteira.
        const int cemKb = 100 * 1024;
        var historico = Criar(maxItens: 2, maxMb: 1);

        for (int i = 0; i < 5; i++)
        {
            var caminho = historico.Adicionar(Bytes(cemKb), Momento(i));
            File.SetLastWriteTimeUtc(caminho, Momento(i).ToUniversalTime());
        }

        Assert.Equal(2, historico.Listar().Count);
    }

    // ---------- Limpeza ----------

    [Fact]
    public void Limpar_ApagaTudo()
    {
        var historico = Criar();
        for (int i = 0; i < 3; i++) historico.Adicionar(Bytes(10), Momento(i));

        historico.Limpar();

        Assert.Empty(historico.Listar());
    }

    [Fact]
    public void Limpar_NaoTocaEmArquivosDeOutraOrigem()
    {
        var alheio = Path.Combine(_pasta, "documento.txt");
        File.WriteAllText(alheio, "não é do AiShot");

        var historico = Criar();
        historico.Adicionar(Bytes(10), Momento(0));
        historico.Limpar();

        Assert.True(File.Exists(alheio));
    }

    [Fact]
    public void Limpar_ComHistoricoVazio_NaoLanca()
    {
        Criar().Limpar();
        Assert.Empty(Criar().Listar());
    }
}
