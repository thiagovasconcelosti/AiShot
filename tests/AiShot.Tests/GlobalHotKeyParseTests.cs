using System.Windows.Forms;
using AiShot.HotKey;

namespace AiShot.Tests;

/// <summary>
/// Interpretação da string de atalho vinda do arquivo de configuração.
/// A entrada é digitada por pessoas e pode chegar malformada; o contrato é que
/// nunca lance e sempre produza uma combinação utilizável.
/// </summary>
public class GlobalHotKeyParseTests
{
    // ---------- Tecla isolada ----------

    [Fact]
    public void Parse_ComPrintScreen_NaoExigeModificadores()
    {
        var c = GlobalHotKey.Parse("PrintScreen");

        Assert.Equal(Keys.PrintScreen, c.Key);
        Assert.False(c.Ctrl);
        Assert.False(c.Alt);
        Assert.False(c.Shift);
        Assert.False(c.Win);
    }

    [Theory]
    [InlineData("PrintScreen")]
    [InlineData("printscreen")]
    [InlineData("PRINTSCREEN")]
    [InlineData("PrtSc")]
    [InlineData("prtsc")]
    [InlineData("PrtScr")]
    public void Parse_ReconheceOsApelidosDePrintScreen(string entrada) =>
        Assert.Equal(Keys.PrintScreen, GlobalHotKey.Parse(entrada).Key);

    [Theory]
    [InlineData("F1", Keys.F1)]
    [InlineData("f12", Keys.F12)]
    [InlineData("A", Keys.A)]
    [InlineData("Insert", Keys.Insert)]
    public void Parse_ReconheceTeclasPeloNomeDoEnum(string entrada, Keys esperada) =>
        Assert.Equal(esperada, GlobalHotKey.Parse(entrada).Key);

    // ---------- Modificadores ----------

    [Fact]
    public void Parse_ComCtrl_MarcaApenasCtrl()
    {
        var c = GlobalHotKey.Parse("Ctrl+S");

        Assert.Equal(Keys.S, c.Key);
        Assert.True(c.Ctrl);
        Assert.False(c.Alt);
        Assert.False(c.Shift);
        Assert.False(c.Win);
    }

    [Theory]
    [InlineData("Ctrl+S")]
    [InlineData("Control+S")]
    [InlineData("CTRL+S")]
    [InlineData("control+s")]
    public void Parse_AceitaCtrlEControlComoSinonimos(string entrada) =>
        Assert.True(GlobalHotKey.Parse(entrada).Ctrl);

    [Theory]
    [InlineData("Win+S")]
    [InlineData("Windows+S")]
    public void Parse_AceitaWinEWindowsComoSinonimos(string entrada) =>
        Assert.True(GlobalHotKey.Parse(entrada).Win);

    [Fact]
    public void Parse_ComTodosOsModificadores_MarcaTodos()
    {
        var c = GlobalHotKey.Parse("Ctrl+Alt+Shift+Win+F5");

        Assert.Equal(Keys.F5, c.Key);
        Assert.True(c.Ctrl);
        Assert.True(c.Alt);
        Assert.True(c.Shift);
        Assert.True(c.Win);
    }

    [Fact]
    public void Parse_NaoDependeDaOrdemDosModificadores()
    {
        Assert.Equal(
            GlobalHotKey.Parse("Ctrl+Alt+F5"),
            GlobalHotKey.Parse("Alt+Ctrl+F5"));
    }

    [Fact]
    public void Parse_ComModificadorRepetido_NaoAlteraOResultado()
    {
        Assert.Equal(
            GlobalHotKey.Parse("Ctrl+F5"),
            GlobalHotKey.Parse("Ctrl+Ctrl+F5"));
    }

    // ---------- Formatação tolerada ----------

    [Theory]
    [InlineData(" Ctrl + Alt + F5 ")]
    [InlineData("Ctrl+  Alt+F5")]
    [InlineData("Ctrl++Alt+F5")]      // separador duplicado
    [InlineData("+Ctrl+Alt+F5+")]     // separador nas bordas
    public void Parse_ToleraEspacosESeparadoresExtras(string entrada)
    {
        var c = GlobalHotKey.Parse(entrada);

        Assert.Equal(Keys.F5, c.Key);
        Assert.True(c.Ctrl);
        Assert.True(c.Alt);
    }

    // ---------- Entradas inválidas ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+")]
    [InlineData("TeclaQueNaoExiste")]
    [InlineData("Ctrl+Alt")]           // só modificadores, sem tecla principal
    public void Parse_ComEntradaInvalida_CaiEmPrintScreen(string? entrada)
    {
        // Um atalho inválido no arquivo não pode deixar o aplicativo sem
        // nenhuma forma de capturar — o padrão assume o lugar.
        Assert.Equal(Keys.PrintScreen, GlobalHotKey.Parse(entrada).Key);
    }

    [Fact]
    public void Parse_ComEntradaInvalida_PreservaOsModificadoresInformados()
    {
        var c = GlobalHotKey.Parse("Ctrl+Alt");

        Assert.Equal(Keys.PrintScreen, c.Key);
        Assert.True(c.Ctrl);
        Assert.True(c.Alt);
    }

    [Fact]
    public void Parse_NuncaLanca()
    {
        var exoticas = new[] { "\t\n", "🎹", "Ctrl+" + new string('x', 500), "++++" };

        foreach (var entrada in exoticas)
        {
            var excecao = Record.Exception(() => GlobalHotKey.Parse(entrada));
            Assert.Null(excecao);
        }
    }

    // ---------- Última tecla vence ----------

    [Fact]
    public void Parse_ComVariasTeclasPrincipais_UsaAUltima()
    {
        // Comportamento atual documentado: não é um erro, a última sobrescreve.
        Assert.Equal(Keys.F2, GlobalHotKey.Parse("F1+F2").Key);
    }
}
