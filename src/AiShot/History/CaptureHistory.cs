using System.Diagnostics;
using System.Globalization;

namespace AiShot.History;

/// <summary>Uma captura guardada no histórico.</summary>
/// <param name="Caminho">Arquivo PNG em disco.</param>
/// <param name="Momento">Quando a captura foi feita.</param>
/// <param name="Bytes">Tamanho do arquivo.</param>
public sealed record CapturaGuardada(string Caminho, DateTime Momento, long Bytes);

/// <summary>
/// Guarda as últimas capturas em disco, aplicando limites de quantidade e de
/// espaço.
/// </summary>
/// <remarks>
/// Capturas de tela carregam o que estava na tela — senhas à mostra, conversas,
/// documentos. Por isso o recurso nasce desligado (ver
/// <see cref="Config.HistoryConfig.Enabled"/>): gravar isso em disco é uma
/// decisão do usuário, não um padrão que ele descobre depois.
/// </remarks>
public sealed class CaptureHistory
{
    /// <summary>Nome dos arquivos: prefixo, carimbo de tempo ordenável, extensão.</summary>
    private const string Prefixo = "aishot_";
    private const string Extensao = ".png";
    private const string FormatoDoCarimbo = "yyyyMMdd_HHmmss_fff";

    private readonly string _pasta;
    private readonly int _maximoDeItens;
    private readonly long _maximoDeBytes;

    /// <summary>Pasta padrão: %LOCALAPPDATA%\AiShot\history.</summary>
    public static string PastaPadrao =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AiShot", "history");

    public CaptureHistory(string pasta, int maximoDeItens, int maximoEmMegabytes)
    {
        _pasta = pasta;
        // Um limite zero ou negativo desligaria o histórico por uma porta que
        // não é a chave de ligar/desligar; um item é o mínimo coerente.
        _maximoDeItens = Math.Max(1, maximoDeItens);
        _maximoDeBytes = Math.Max(1, maximoEmMegabytes) * 1024L * 1024L;
    }

    /// <summary>Grava a captura e aplica os limites. Devolve o arquivo criado.</summary>
    public string Adicionar(byte[] png, DateTime momento)
    {
        Directory.CreateDirectory(_pasta);

        var nome = Prefixo + momento.ToString(FormatoDoCarimbo, CultureInfo.InvariantCulture) + Extensao;
        var caminho = Path.Combine(_pasta, nome);

        // Duas capturas no mesmo milissegundo sobrescreveriam uma à outra;
        // um sufixo resolve sem depender do relógio ter resolução maior.
        int sufixo = 1;
        while (File.Exists(caminho))
        {
            nome = Prefixo + momento.ToString(FormatoDoCarimbo, CultureInfo.InvariantCulture)
                   + "_" + sufixo.ToString(CultureInfo.InvariantCulture) + Extensao;
            caminho = Path.Combine(_pasta, nome);
            sufixo++;
        }

        File.WriteAllBytes(caminho, png);
        AplicarLimites();
        return caminho;
    }

    /// <summary>Capturas guardadas, da mais recente para a mais antiga.</summary>
    public IReadOnlyList<CapturaGuardada> Listar()
    {
        if (!Directory.Exists(_pasta)) return [];

        var itens = new List<CapturaGuardada>();
        foreach (var caminho in Directory.EnumerateFiles(_pasta, Prefixo + "*" + Extensao))
        {
            try
            {
                var info = new FileInfo(caminho);
                itens.Add(new CapturaGuardada(caminho, info.LastWriteTimeUtc, info.Length));
            }
            catch (Exception ex)
            {
                // Arquivo removido ou bloqueado entre a listagem e a leitura:
                // some da lista em vez de derrubar a abertura do menu.
                Debug.WriteLine($"CaptureHistory: ignorando '{caminho}': {ex.Message}");
            }
        }

        itens.Sort((a, b) => b.Momento.CompareTo(a.Momento));
        return itens;
    }

    /// <summary>Apaga tudo que está guardado.</summary>
    public void Limpar()
    {
        foreach (var item in Listar()) Apagar(item.Caminho);
    }

    /// <summary>
    /// Remove o excedente: primeiro pela contagem, depois pelo espaço, sempre
    /// descartando as capturas mais antigas.
    /// </summary>
    private void AplicarLimites()
    {
        var itens = Listar();

        for (int i = _maximoDeItens; i < itens.Count; i++) Apagar(itens[i].Caminho);

        long acumulado = 0;
        for (int i = 0; i < Math.Min(_maximoDeItens, itens.Count); i++)
        {
            acumulado += itens[i].Bytes;
            // A captura mais recente fica mesmo se sozinha estourar o limite:
            // apagá-la na hora esvaziaria o histórico logo depois de gravar.
            if (i > 0 && acumulado > _maximoDeBytes) Apagar(itens[i].Caminho);
        }
    }

    private static void Apagar(string caminho)
    {
        try { File.Delete(caminho); }
        catch (Exception ex) { Debug.WriteLine($"CaptureHistory: não removeu '{caminho}': {ex.Message}"); }
    }
}
