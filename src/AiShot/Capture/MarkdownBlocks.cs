namespace AiShot.Capture;

/// <summary>Tipo de bloco reconhecido no texto da resposta.</summary>
internal enum BlockKind
{
    /// <summary>Parágrafo comum.</summary>
    Paragraph,

    /// <summary>Bloco de código delimitado por três crases.</summary>
    Code,

    /// <summary>Item de lista, com marcador ou numerado.</summary>
    ListItem,
}

/// <summary>Um trecho da resposta, já classificado para desenho.</summary>
/// <param name="Kind">Como o trecho deve ser apresentado.</param>
/// <param name="Text">Conteúdo, sem os marcadores da sintaxe.</param>
/// <param name="Language">Linguagem declarada no bloco de código, quando houver.</param>
/// <param name="Marker">
/// Marcador do item de lista. Traz o número original em listas numeradas — sem
/// isso, "1." e "2." virariam dois marcadores iguais e a ordem se perderia.
/// </param>
internal sealed record Block(BlockKind Kind, string Text, string? Language = null, string? Marker = null);

/// <summary>
/// Divide a resposta da IA nos blocos que o chat sabe desenhar.
/// </summary>
/// <remarks>
/// Reconhece o subconjunto que de fato aparece nas respostas — blocos de código,
/// listas e parágrafos — em vez de implementar Markdown por inteiro. O objetivo
/// é que um trecho de código chegue legível ao usuário, não renderizar um
/// documento.
/// </remarks>
internal static class MarkdownBlocks
{
    private const string Cerca = "```";

    /// <summary>Separa o texto em blocos, preservando a ordem original.</summary>
    public static List<Block> Parse(string? texto)
    {
        var blocos = new List<Block>();
        if (string.IsNullOrEmpty(texto)) return blocos;

        var linhas = texto.Replace("\r\n", "\n").Split('\n');
        var paragrafo = new List<string>();

        void FecharParagrafo()
        {
            if (paragrafo.Count == 0) return;
            var conteudo = string.Join("\n", paragrafo).Trim();
            if (conteudo.Length > 0) blocos.Add(new Block(BlockKind.Paragraph, conteudo));
            paragrafo.Clear();
        }

        for (int i = 0; i < linhas.Length; i++)
        {
            var linha = linhas[i];

            if (linha.TrimStart().StartsWith(Cerca, StringComparison.Ordinal))
            {
                FecharParagrafo();

                var linguagem = linha.TrimStart()[Cerca.Length..].Trim();
                var codigo = new List<string>();

                // Consome até a cerca de fechamento. Sem ela, o bloco vai até o
                // fim do texto — uma resposta truncada não pode virar exceção.
                i++;
                while (i < linhas.Length && !linhas[i].TrimStart().StartsWith(Cerca, StringComparison.Ordinal))
                {
                    codigo.Add(linhas[i]);
                    i++;
                }

                blocos.Add(new Block(
                    BlockKind.Code,
                    string.Join("\n", codigo).TrimEnd(),
                    string.IsNullOrWhiteSpace(linguagem) ? null : linguagem));
                continue;
            }

            var semEspaco = linha.TrimStart();
            if (EhItemDeLista(semEspaco, out var conteudoDoItem, out var marcador))
            {
                FecharParagrafo();
                blocos.Add(new Block(BlockKind.ListItem, conteudoDoItem, Marker: marcador));
                continue;
            }

            if (semEspaco.Length == 0) { FecharParagrafo(); continue; }

            paragrafo.Add(linha);
        }

        FecharParagrafo();
        return blocos;
    }

    /// <summary>
    /// Reconhece "- item", "* item" e "1. item". O marcador precisa vir seguido
    /// de espaço, para não confundir com um traço no início de uma frase.
    /// </summary>
    private static bool EhItemDeLista(string linha, out string conteudo, out string marcador)
    {
        conteudo = "";
        marcador = "•";

        if (linha.Length >= 2 && (linha[0] == '-' || linha[0] == '*') && linha[1] == ' ')
        {
            conteudo = linha[2..].Trim();
            return conteudo.Length > 0;
        }

        // Lista numerada: dígitos, ponto e espaço.
        int digitos = 0;
        while (digitos < linha.Length && char.IsAsciiDigit(linha[digitos])) digitos++;

        if (digitos > 0 && digitos + 1 < linha.Length && linha[digitos] == '.' && linha[digitos + 1] == ' ')
        {
            conteudo = linha[(digitos + 2)..].Trim();
            marcador = linha[..(digitos + 1)]; // preserva "1.", "42." etc.
            return conteudo.Length > 0;
        }

        return false;
    }

    /// <summary>
    /// Remove a ênfase em negrito e o código em linha do texto, deixando apenas
    /// o conteúdo. O chat desenha com uma fonte só; manter os asteriscos seria
    /// pior que descartá-los.
    /// </summary>
    public static string StripInlineMarkup(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";

        var saida = new System.Text.StringBuilder(texto.Length);
        for (int i = 0; i < texto.Length; i++)
        {
            // ** ou __ (negrito) e * ou _ (itálico) são descartados; o acento
            // grave delimita código em linha.
            if (texto[i] == '`') continue;
            if (texto[i] == '*' || texto[i] == '_')
            {
                // Preserva o caractere quando ele faz parte da palavra
                // (snake_case, nome_de_variável), e não de uma marcação.
                bool anteriorEhLetra = i > 0 && char.IsLetterOrDigit(texto[i - 1]);
                bool proximoEhLetra = i + 1 < texto.Length && char.IsLetterOrDigit(texto[i + 1]);
                if (texto[i] == '_' && anteriorEhLetra && proximoEhLetra) { saida.Append(texto[i]); continue; }
                continue;
            }
            saida.Append(texto[i]);
        }
        return saida.ToString();
    }
}
