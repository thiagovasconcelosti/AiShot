using AiShot.Capture;

namespace AiShot.Tests;

/// <summary>
/// Separação da resposta da IA nos blocos que o chat desenha.
/// </summary>
public class MarkdownBlocksTests
{
    // ---------- Parágrafos ----------

    [Fact]
    public void Parse_ComTextoSimples_ProduzUmParagrafo()
    {
        var blocos = MarkdownBlocks.Parse("Uma resposta curta.");

        var b = Assert.Single(blocos);
        Assert.Equal(BlockKind.Paragraph, b.Kind);
        Assert.Equal("Uma resposta curta.", b.Text);
    }

    [Fact]
    public void Parse_ComLinhaEmBranco_SeparaParagrafos()
    {
        var blocos = MarkdownBlocks.Parse("Primeiro.\n\nSegundo.");

        Assert.Equal(2, blocos.Count);
        Assert.All(blocos, b => Assert.Equal(BlockKind.Paragraph, b.Kind));
        Assert.Equal("Primeiro.", blocos[0].Text);
        Assert.Equal("Segundo.", blocos[1].Text);
    }

    [Fact]
    public void Parse_JuntaLinhasConsecutivasNoMesmoParagrafo()
    {
        var blocos = MarkdownBlocks.Parse("linha um\nlinha dois");

        Assert.Single(blocos);
        Assert.Equal("linha um\nlinha dois", blocos[0].Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    public void Parse_SemConteudo_NaoProduzBlocos(string? texto) =>
        Assert.Empty(MarkdownBlocks.Parse(texto));

    // ---------- Blocos de código ----------

    [Fact]
    public void Parse_ReconheceBlocoDeCodigo()
    {
        var blocos = MarkdownBlocks.Parse("```\nvar x = 1;\n```");

        var b = Assert.Single(blocos);
        Assert.Equal(BlockKind.Code, b.Kind);
        Assert.Equal("var x = 1;", b.Text);
    }

    [Fact]
    public void Parse_GuardaALinguagemDoBloco()
    {
        var blocos = MarkdownBlocks.Parse("```csharp\nvar x = 1;\n```");

        Assert.Equal("csharp", Assert.Single(blocos).Language);
    }

    [Fact]
    public void Parse_SemLinguagemDeclarada_DeixaOCampoNulo()
    {
        var blocos = MarkdownBlocks.Parse("```\ncódigo\n```");

        Assert.Null(Assert.Single(blocos).Language);
    }

    [Fact]
    public void Parse_PreservaAIndentacaoDoCodigo()
    {
        var blocos = MarkdownBlocks.Parse("```\nif (x) {\n    y();\n}\n```");

        Assert.Equal("if (x) {\n    y();\n}", Assert.Single(blocos).Text);
    }

    [Fact]
    public void Parse_PreservaLinhasEmBrancoDentroDoCodigo()
    {
        var blocos = MarkdownBlocks.Parse("```\nprimeira\n\nterceira\n```");

        Assert.Equal("primeira\n\nterceira", Assert.Single(blocos).Text);
    }

    [Fact]
    public void Parse_ComCodigoSemFechamento_NaoLancaEUsaOResto()
    {
        // Uma resposta truncada no meio do bloco não pode virar exceção.
        var blocos = MarkdownBlocks.Parse("```python\nprint(1)\nprint(2)");

        var b = Assert.Single(blocos);
        Assert.Equal(BlockKind.Code, b.Kind);
        Assert.Equal("print(1)\nprint(2)", b.Text);
    }

    [Fact]
    public void Parse_IntercalaTextoECodigoNaOrdemOriginal()
    {
        var blocos = MarkdownBlocks.Parse("Explicação:\n```\ncódigo\n```\nDepois disso.");

        Assert.Equal(3, blocos.Count);
        Assert.Equal(BlockKind.Paragraph, blocos[0].Kind);
        Assert.Equal(BlockKind.Code, blocos[1].Kind);
        Assert.Equal(BlockKind.Paragraph, blocos[2].Kind);
    }

    [Fact]
    public void Parse_ComDoisBlocosDeCodigo_ReconheceOsDois()
    {
        var blocos = MarkdownBlocks.Parse("```\na\n```\ntexto\n```\nb\n```");

        Assert.Equal(2, blocos.Count(b => b.Kind == BlockKind.Code));
    }

    // ---------- Listas ----------

    [Theory]
    [InlineData("- primeiro")]
    [InlineData("* primeiro")]
    [InlineData("1. primeiro")]
    [InlineData("42. primeiro")]
    public void Parse_ReconheceItensDeLista(string linha)
    {
        var blocos = MarkdownBlocks.Parse(linha);

        var b = Assert.Single(blocos);
        Assert.Equal(BlockKind.ListItem, b.Kind);
        Assert.Equal("primeiro", b.Text);
    }

    [Fact]
    public void Parse_ComVariosItens_ProduzUmBlocoPorItem()
    {
        var blocos = MarkdownBlocks.Parse("- um\n- dois\n- três");

        Assert.Equal(3, blocos.Count);
        Assert.All(blocos, b => Assert.Equal(BlockKind.ListItem, b.Kind));
    }

    [Theory]
    [InlineData("-sem espaço depois do traço")]
    [InlineData("1.sem espaço depois do ponto")]
    [InlineData("- ")]
    public void Parse_NaoConfundeTextoComumComItemDeLista(string linha)
    {
        var blocos = MarkdownBlocks.Parse(linha);

        Assert.DoesNotContain(blocos, b => b.Kind == BlockKind.ListItem);
    }

    [Theory]
    [InlineData("- item", "•")]
    [InlineData("* item", "•")]
    [InlineData("1. item", "1.")]
    [InlineData("42. item", "42.")]
    public void Parse_PreservaONumeroDasListasNumeradas(string linha, string marcadorEsperado)
    {
        // Sem isso, "1." e "2." virariam dois marcadores iguais e a ordem
        // — que é o ponto de uma lista numerada — se perderia.
        Assert.Equal(marcadorEsperado, Assert.Single(MarkdownBlocks.Parse(linha)).Marker);
    }

    [Fact]
    public void Parse_NaoTrataTracoNoMeioDaFraseComoLista()
    {
        var blocos = MarkdownBlocks.Parse("um traço - no meio da frase");

        Assert.Equal(BlockKind.Paragraph, Assert.Single(blocos).Kind);
    }

    // ---------- Marcação em linha ----------

    [Fact]
    public void StripInlineMarkup_RemoveNegrito() =>
        Assert.Equal("texto importante", MarkdownBlocks.StripInlineMarkup("**texto importante**"));

    [Fact]
    public void StripInlineMarkup_RemoveCodigoEmLinha() =>
        Assert.Equal("use a variável x aqui", MarkdownBlocks.StripInlineMarkup("use a variável `x` aqui"));

    [Fact]
    public void StripInlineMarkup_PreservaSublinhadoDentroDePalavra()
    {
        // snake_case e nomes de variáveis não são ênfase.
        Assert.Equal("nome_da_variavel", MarkdownBlocks.StripInlineMarkup("nome_da_variavel"));
    }

    [Fact]
    public void StripInlineMarkup_RemoveEnfaseNasBordasDaPalavra() =>
        Assert.Equal("enfase", MarkdownBlocks.StripInlineMarkup("_enfase_"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void StripInlineMarkup_ComEntradaVazia_DevolveVazio(string? texto) =>
        Assert.Equal("", MarkdownBlocks.StripInlineMarkup(texto));

    [Fact]
    public void StripInlineMarkup_NaoAlteraTextoSemMarcacao()
    {
        const string texto = "Uma frase comum, com vírgula e ponto.";

        Assert.Equal(texto, MarkdownBlocks.StripInlineMarkup(texto));
    }

    // ---------- Terminação de linha ----------

    [Fact]
    public void Parse_AceitaTerminacaoDeLinhaDoWindows()
    {
        var blocos = MarkdownBlocks.Parse("primeiro\r\n\r\nsegundo");

        Assert.Equal(2, blocos.Count);
        Assert.Equal("primeiro", blocos[0].Text);
        Assert.Equal("segundo", blocos[1].Text);
    }
}
