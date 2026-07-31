namespace AiShot.Capture;

/// <summary>
/// Percurso do foco do teclado pelos botões das duas barras.
/// </summary>
/// <remarks>
/// Puro, sem UI: a ordem em que Tab caminha é a única regra aqui, e mantê-la
/// separada do desenho permite verificá-la sem abrir uma janela.
///
/// A barra inferior vem antes da lateral porque é onde estão as ações que
/// concluem a captura — copiar, salvar, enviar. Quem chega pelo teclado quer
/// primeiro terminar, não desenhar.
/// </remarks>
internal sealed class KeyboardFocus
{
    private readonly List<string> _ordem = new();
    private int _indice = -1;

    /// <summary>Identificador do botão focado, ou null quando nada tem foco.</summary>
    public string? Focado => _indice >= 0 && _indice < _ordem.Count ? _ordem[_indice] : null;

    /// <summary>Se algum botão está com o foco do teclado.</summary>
    public bool TemFoco => Focado is not null;

    /// <summary>
    /// Atualiza o percurso com os botões atualmente desenhados, preservando o
    /// foco quando o botão focado continua existindo.
    /// </summary>
    /// <remarks>
    /// As barras são remontadas a cada quadro e mudam de conteúdo (a paleta
    /// abre, a seleção muda de lugar). Guardar o índice em vez do
    /// identificador faria o foco pular para outro botão quando a lista
    /// mudasse de tamanho.
    /// </remarks>
    public void Atualizar(IEnumerable<string> acoes, IEnumerable<string> ferramentas)
    {
        var anterior = Focado;

        _ordem.Clear();
        _ordem.AddRange(acoes);
        _ordem.AddRange(ferramentas);

        _indice = anterior is null ? -1 : _ordem.IndexOf(anterior);
    }

    /// <summary>
    /// Move o foco para o próximo botão (ou o anterior, com
    /// <paramref name="paraTras"/>). Devolve false quando não há botões.
    /// </summary>
    /// <remarks>
    /// O percurso é circular: sair pelo fim volta ao começo. O overlay cobre a
    /// tela inteira e não há para onde o foco ir, então deixá-lo escapar
    /// significaria perdê-lo sem aviso.
    /// </remarks>
    public bool Mover(bool paraTras = false)
    {
        if (_ordem.Count == 0) { _indice = -1; return false; }

        if (_indice < 0)
        {
            // Primeiro Tab entra pelo começo; primeiro Shift+Tab, pelo fim.
            _indice = paraTras ? _ordem.Count - 1 : 0;
            return true;
        }

        _indice = paraTras
            ? (_indice - 1 + _ordem.Count) % _ordem.Count
            : (_indice + 1) % _ordem.Count;
        return true;
    }

    /// <summary>Retira o foco de qualquer botão.</summary>
    public void Limpar() => _indice = -1;
}
