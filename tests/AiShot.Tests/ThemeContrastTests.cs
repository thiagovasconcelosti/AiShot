using System.Drawing;
using AiShot.UI;

namespace AiShot.Tests;

/// <summary>
/// Contraste da paleta, conforme a WCAG (Web Content Accessibility Guidelines).
/// </summary>
/// <remarks>
/// Fixa as combinações que a interface de fato desenha. Uma cor trocada sem
/// medir o efeito passa a falhar aqui em vez de degradar a legibilidade em
/// silêncio.
/// </remarks>
public class ThemeContrastTests
{
    /// <summary>Mínimo da WCAG para texto normal (nível AA).</summary>
    private const double MinimoTexto = 4.5;

    /// <summary>Mínimo para componentes de interface e indicadores de foco.</summary>
    private const double MinimoComponente = 3.0;

    /// <summary>
    /// As barras têm alfa 245 e ficam sobre o print escurecido. O pior caso —
    /// o fundo mais escuro possível — é o que vale medir.
    /// </summary>
    private static Color SuperficieReal => Theme.Achatar(Theme.Surface, Color.Black);

    // ---------- Cálculo ----------

    [Fact]
    public void ContrastRatio_ComPretoEBranco_DaOMaximo() =>
        Assert.Equal(21.0, Theme.ContrastRatio(Color.Black, Color.White), precision: 1);

    [Fact]
    public void ContrastRatio_ComCoresIguais_DaOMinimo() =>
        Assert.Equal(1.0, Theme.ContrastRatio(Color.Gray, Color.Gray), precision: 3);

    [Fact]
    public void ContrastRatio_NaoDependeDaOrdemDosArgumentos() =>
        Assert.Equal(
            Theme.ContrastRatio(Theme.Text, Theme.SurfaceHover),
            Theme.ContrastRatio(Theme.SurfaceHover, Theme.Text),
            precision: 6);

    [Fact]
    public void Achatar_ComCorOpaca_DevolveAPropriaCor()
    {
        var opaca = Color.FromArgb(255, 10, 20, 30);

        var achatada = Theme.Achatar(opaca, Color.White);

        Assert.Equal(opaca.ToArgb(), achatada.ToArgb());
    }

    [Fact]
    public void Achatar_ComCorTotalmenteTransparente_DevolveOFundo()
    {
        var achatada = Theme.Achatar(Color.FromArgb(0, 255, 0, 0), Color.FromArgb(255, 10, 20, 30));

        Assert.Equal(Color.FromArgb(255, 10, 20, 30).ToArgb(), achatada.ToArgb());
    }

    [Fact]
    public void Achatar_SempreDevolveCorOpaca()
    {
        // Uma cor achatada entra no cálculo de contraste, que exige opacidade.
        Assert.Equal(255, Theme.Achatar(Color.FromArgb(100, 200, 100, 50), Color.Black).A);
    }

    // ---------- Texto sobre as superfícies desenhadas ----------

    [Theory]
    [InlineData("barra")]
    [InlineData("balão do assistente")]
    [InlineData("campo de texto")]
    public void Texto_AtendeAoMinimoDaWcag(string superficie)
    {
        var fundo = superficie switch
        {
            "barra" => SuperficieReal,
            "balão do assistente" => Theme.SurfaceHover,
            _ => Theme.InputBackground,
        };

        var razao = Theme.ContrastRatio(Theme.Text, fundo);

        Assert.True(razao >= MinimoTexto,
            $"Texto sobre {superficie}: {razao:F2}:1, abaixo do mínimo de {MinimoTexto}:1.");
    }

    [Theory]
    [InlineData("barra")]
    [InlineData("balão do assistente")]
    [InlineData("campo de texto")]
    public void TextoMutado_AtendeAoMinimoDaWcag(string superficie)
    {
        // O texto secundário (dica de ferramenta, dimensões, aviso de cópia) é
        // o candidato natural a ficar abaixo do mínimo.
        var fundo = superficie switch
        {
            "barra" => SuperficieReal,
            "balão do assistente" => Theme.SurfaceHover,
            _ => Theme.InputBackground,
        };

        var razao = Theme.ContrastRatio(Theme.TextMuted, fundo);

        Assert.True(razao >= MinimoTexto,
            $"TextMuted sobre {superficie}: {razao:F2}:1, abaixo do mínimo de {MinimoTexto}:1.");
    }

    [Fact]
    public void TextoDoBalaoDoUsuario_AtendeAoMinimoDaWcag()
    {
        // O balão do usuário é branco com texto preto (ver ChatPanel).
        var razao = Theme.ContrastRatio(Color.Black, Color.White);

        Assert.True(razao >= MinimoTexto);
    }

    // ---------- Indicador de foco ----------

    [Theory]
    [InlineData("barra")]
    [InlineData("botão sob o cursor")]
    public void IndicadorDeFoco_AtendeAoMinimoDeComponente(string superficie)
    {
        // O contorno de foco precisa ser percebido contra o que estiver atrás
        // dele — é o que diz a quem navega por teclado onde está.
        var fundo = superficie == "barra" ? SuperficieReal : Theme.SurfaceHover;

        var razao = Theme.ContrastRatio(Theme.FocusRing, fundo);

        Assert.True(razao >= MinimoComponente,
            $"FocusRing sobre {superficie}: {razao:F2}:1, abaixo do mínimo de {MinimoComponente}:1.");
    }

    [Fact]
    public void IndicadorDeFoco_SeDistingueDaBordaComum()
    {
        // Se o contorno de foco parecesse com a borda de repouso, o foco não
        // seria percebido — que é o defeito que o indicador existe para evitar.
        var foco = Theme.ContrastRatio(Theme.FocusRing, SuperficieReal);
        var borda = Theme.ContrastRatio(Theme.Border, SuperficieReal);

        Assert.True(foco > borda * 2,
            $"O contorno de foco ({foco:F2}:1) precisa se destacar da borda comum ({borda:F2}:1).");
    }
}
