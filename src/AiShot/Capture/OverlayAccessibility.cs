using System.Drawing;
using System.Windows.Forms;

namespace AiShot.Capture;

/// <summary>
/// Expõe os botões das barras à tecnologia assistiva.
/// </summary>
/// <remarks>
/// O overlay desenha tudo com <c>Graphics.DrawString</c> sobre um formulário sem
/// controles, então não há nada que o Narrador possa encontrar sozinho. Esta
/// árvore descreve cada botão como um filho nomeado, com a mesma posição e o
/// mesmo nome que o usuário vidente enxerga.
/// </remarks>
internal sealed class OverlayAccessibility : Control.ControlAccessibleObject
{
    private readonly Func<IReadOnlyList<IconButton>> _botoes;
    private readonly Func<string?> _focado;
    private readonly Action<string> _acionar;
    private readonly Func<Point> _origemNaTela;

    public OverlayAccessibility(
        Control dono,
        Func<IReadOnlyList<IconButton>> botoes,
        Func<string?> focado,
        Action<string> acionar,
        Func<Point> origemNaTela)
        : base(dono)
    {
        _botoes = botoes;
        _focado = focado;
        _acionar = acionar;
        _origemNaTela = origemNaTela;
    }

    public override AccessibleRole Role => AccessibleRole.ToolBar;

    public override int GetChildCount() => _botoes().Count;

    public override AccessibleObject? GetChild(int index)
    {
        var botoes = _botoes();
        if (index < 0 || index >= botoes.Count) return null;
        return new BotaoAcessivel(this, botoes[index], _focado, _acionar, _origemNaTela);
    }

    /// <summary>Um botão da barra, visto pela tecnologia assistiva.</summary>
    private sealed class BotaoAcessivel : AccessibleObject
    {
        private readonly AccessibleObject _pai;
        private readonly IconButton _botao;
        private readonly Func<string?> _focado;
        private readonly Action<string> _acionar;
        private readonly Func<Point> _origemNaTela;

        public BotaoAcessivel(
            AccessibleObject pai,
            IconButton botao,
            Func<string?> focado,
            Action<string> acionar,
            Func<Point> origemNaTela)
        {
            _pai = pai;
            _botao = botao;
            _focado = focado;
            _acionar = acionar;
            _origemNaTela = origemNaTela;
        }

        public override AccessibleObject Parent => _pai;

        public override AccessibleRole Role => AccessibleRole.PushButton;

        /// <summary>
        /// A dica que já acompanha o botão. É o mesmo texto que o usuário
        /// vidente lê ao passar o cursor, incluindo o atalho entre parênteses.
        /// </summary>
        public override string Name => _botao.Tip;

        public override AccessibleStates State
        {
            get
            {
                var estados = AccessibleStates.Focusable;
                if (_botao.Active) estados |= AccessibleStates.Checked;
                if (_focado() == _botao.Id) estados |= AccessibleStates.Focused;
                return estados;
            }
        }

        /// <summary>
        /// Retângulo em coordenadas de tela. O overlay cobre a área virtual
        /// inteira, e os retângulos dos botões são relativos a ela — sem somar
        /// a origem, o Narrador apontaria para o lugar errado num arranjo com
        /// mais de um monitor.
        /// </summary>
        public override Rectangle Bounds
        {
            get
            {
                var origem = _origemNaTela();
                return new Rectangle(
                    _botao.Rect.X + origem.X,
                    _botao.Rect.Y + origem.Y,
                    _botao.Rect.Width,
                    _botao.Rect.Height);
            }
        }

        public override void DoDefaultAction() => _acionar(_botao.Id);
    }
}
