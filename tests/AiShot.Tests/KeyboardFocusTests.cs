using AiShot.Capture;

namespace AiShot.Tests;

/// <summary>
/// Percurso do foco do teclado pelos botões das barras.
/// </summary>
public class KeyboardFocusTests
{
    private static readonly string[] Acoes = ["copy", "ocr", "save", "close"];
    private static readonly string[] Ferramentas = ["pen", "arrow", "color"];

    private static KeyboardFocus Montado()
    {
        var foco = new KeyboardFocus();
        foco.Atualizar(Acoes, Ferramentas);
        return foco;
    }

    // ---------- Estado inicial ----------

    [Fact]
    public void SemMover_NadaTemFoco()
    {
        var foco = Montado();

        Assert.Null(foco.Focado);
        Assert.False(foco.TemFoco);
    }

    [Fact]
    public void Mover_SemBotoes_DevolveFalso()
    {
        // Durante a seleção não há barras desenhadas; Tab não pode travar.
        var foco = new KeyboardFocus();

        Assert.False(foco.Mover());
        Assert.Null(foco.Focado);
    }

    // ---------- Ordem ----------

    [Fact]
    public void PrimeiroTab_EntraPelaBarraDeAcoes()
    {
        // As ações vêm antes das ferramentas: quem chega pelo teclado quer
        // primeiro concluir a captura, não desenhar.
        var foco = Montado();

        foco.Mover();

        Assert.Equal("copy", foco.Focado);
    }

    [Fact]
    public void PrimeiroShiftTab_EntraPeloFim()
    {
        var foco = Montado();

        foco.Mover(paraTras: true);

        Assert.Equal("color", foco.Focado);
    }

    [Fact]
    public void Tab_PercorreAcoesDepoisFerramentas()
    {
        var foco = Montado();
        var visitados = new List<string>();

        for (int i = 0; i < Acoes.Length + Ferramentas.Length; i++)
        {
            foco.Mover();
            visitados.Add(foco.Focado!);
        }

        Assert.Equal([.. Acoes, .. Ferramentas], visitados);
    }

    [Fact]
    public void Tab_AoChegarAoFim_VoltaAoComeco()
    {
        // O overlay cobre a tela toda; deixar o foco escapar seria perdê-lo.
        var foco = Montado();
        for (int i = 0; i < Acoes.Length + Ferramentas.Length; i++) foco.Mover();

        foco.Mover();

        Assert.Equal("copy", foco.Focado);
    }

    [Fact]
    public void ShiftTab_NoPrimeiro_VaiParaOUltimo()
    {
        var foco = Montado();
        foco.Mover();

        foco.Mover(paraTras: true);

        Assert.Equal("color", foco.Focado);
    }

    [Fact]
    public void ShiftTab_DesfazOTab()
    {
        var foco = Montado();
        foco.Mover();
        foco.Mover();

        foco.Mover(paraTras: true);

        Assert.Equal("copy", foco.Focado);
    }

    // ---------- Remontagem das barras ----------

    [Fact]
    public void Atualizar_PreservaOBotaoFocado()
    {
        // As barras são remontadas a cada quadro; o foco não pode pular.
        var foco = Montado();
        foco.Mover();
        foco.Mover(); // "ocr"

        foco.Atualizar(Acoes, Ferramentas);

        Assert.Equal("ocr", foco.Focado);
    }

    [Fact]
    public void Atualizar_ComOBotaoFocadoAusente_SoltaOFoco()
    {
        // A paleta fecha e o botão some: manter o índice apontaria para outro
        // botão, e o usuário veria o foco saltar sem ter pedido.
        var foco = Montado();
        foco.Mover(paraTras: true); // "color"

        foco.Atualizar(Acoes, ["pen", "arrow"]);

        Assert.Null(foco.Focado);
    }

    [Fact]
    public void Atualizar_ComListaMaior_MantemOFocoNoMesmoBotao()
    {
        var foco = Montado();
        foco.Mover(); // "copy"

        foco.Atualizar(["novo", .. Acoes], Ferramentas);

        Assert.Equal("copy", foco.Focado);
    }

    [Fact]
    public void Atualizar_DepoisDeSoltarOFoco_ContinuaSemFoco()
    {
        var foco = Montado();

        foco.Atualizar(Acoes, Ferramentas);

        Assert.False(foco.TemFoco);
    }

    // ---------- Limpar ----------

    [Fact]
    public void Limpar_SoltaOFoco()
    {
        var foco = Montado();
        foco.Mover();

        foco.Limpar();

        Assert.Null(foco.Focado);
    }

    [Fact]
    public void Limpar_DepoisDeLimpar_TabEntraPeloComeco()
    {
        var foco = Montado();
        foco.Mover();
        foco.Mover();
        foco.Limpar();

        foco.Mover();

        Assert.Equal("copy", foco.Focado);
    }
}
