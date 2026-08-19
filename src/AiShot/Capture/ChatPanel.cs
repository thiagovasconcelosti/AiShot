using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using AiShot.Ai;
using AiShot.App;
using AiShot.Resources;
using AiShot.UI;

namespace AiShot.Capture;

/// <summary>
/// Balão de chat com a IA sobre o print: timeline (enviado/recebido), scroll,
/// input e sessão contínua. Componente auto-contido hospedado pelo overlay.
/// </summary>
internal sealed class ChatPanel
{
    private static readonly Font MsgFont = new("Segoe UI", 10f);
    private static readonly Font HeaderFont = new("Segoe UI Semibold", 10.5f);
    private static readonly Font LinkFont = new("Segoe UI", 8.5f, FontStyle.Underline);
    private static readonly StringFormat CenterFmt = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    /// <summary>
    /// Fonte dos blocos de código: monoespaçada, para que a indentação faça
    /// sentido. O GDI+ substitui silenciosamente uma família ausente pela fonte
    /// padrão — o que descaracterizaria o bloco —, então a escolha é conferida
    /// contra as famílias instaladas.
    /// </summary>
    private static readonly Font CodeFont = EscolherFonteMonoespacada(9f);

    private static Font EscolherFonteMonoespacada(float tamanho)
    {
        // Da mais desejável à mais garantida. Consolas acompanha o Windows
        // desde o Vista; Courier New é o último recurso.
        foreach (var nome in new[] { "Cascadia Mono", "Consolas", "Courier New" })
        {
            try
            {
                using var familia = new FontFamily(nome);
                return new Font(familia, tamanho);
            }
            catch (ArgumentException) { /* família ausente: tenta a próxima */ }
        }
        return new Font(FontFamily.GenericMonospace, tamanho);
    }

    /// <summary>Recuo do texto dos itens de lista, depois do marcador.</summary>
    private const int RecuoDaLista = 14;

    private readonly Form _host;
    private readonly Func<IAiChatSession> _startSession;
    private readonly CancellationToken _ct;

    private IAiChatSession? _session;
    // Fonte única do diálogo é _session.History; aqui só guardamos um erro transitório
    // (a falha de um turno não entra no histórico da sessão, mas precisa aparecer no chat).
    private string? _pendingError;

    /// <summary>
    /// Resposta que está chegando por streaming, ainda não registrada no
    /// histórico. Desenhada como um balão do assistente enquanto cresce; volta a
    /// null quando o turno termina e a sessão assume o texto definitivo.
    /// </summary>
    private string? _respostaParcial;
    private bool _busy;
    private bool _scrollToBottom;
    private int _scroll, _contentHeight;
    private Rectangle _bubble, _viewport, _sendBtn, _privacyBtn, _reportBtn;

    /// <summary>
    /// Botões de copiar por resposta, recalculados a cada desenho junto com as
    /// posições dos balões.
    /// </summary>
    private readonly List<(Rectangle Area, string Texto)> _copyButtons = new();
    private TextBox? _input;

    public bool IsOpen { get; private set; }

    public ChatPanel(Form host, Func<IAiChatSession> startSession, CancellationToken ct)
    {
        _host = host;
        _startSession = startSession;
        _ct = ct;
    }

    // ---------- Ciclo ----------
    public void Open()
    {
        IsOpen = true;
        try { _session ??= _startSession(); } // snapshot da imagem no momento da abertura
        catch (OperationCanceledException)
        {
            IsOpen = false;
            return;
        }
        EnsureInput();
        _input!.Visible = true;
        _input.Focus();
        _host.Invalidate();
    }

    public void Close()
    {
        IsOpen = false;
        if (_input is not null) _input.Visible = false;
        _host.Invalidate();
    }

    // ---------- Entrada ----------
    /// <summary>Processa clique; retorna true se consumido pelo chat.</summary>
    public bool OnMouseDown(Point p)
    {
        if (!IsOpen) return false;
        if (_sendBtn.Contains(p)) { _ = SendAsync(); return true; }
        if (_privacyBtn.Contains(p)) { OpenExternal(ComplianceLinks.PrivacyPolicy); return true; }
        if (_reportBtn.Contains(p))
        {
            OpenExternal(ComplianceLinks.BuildReportUri(Strings.ChatReportSubject));
            return true;
        }

        foreach (var (area, texto) in _copyButtons)
            if (area.Contains(p)) { CopiarResposta(texto); return true; }

        return _bubble.Contains(p); // clique dentro do balão não vaza pro overlay
    }

    /// <summary>
    /// Copia a resposta para a área de transferência. O texto do balão é
    /// desenhado como pixels, então esta é a única forma de o usuário levá-lo.
    /// </summary>
    private void CopiarResposta(string texto)
    {
        try
        {
            Clipboard.SetText(texto);
            _aviso = Strings.ChatAnswerCopied;
        }
        catch (Exception ex)
        {
            _aviso = "Não foi possível copiar: " + ex.Message;
        }
        _avisoAte = DateTime.UtcNow.AddSeconds(2);
        _host.Invalidate();
    }

    private string? _aviso;
    private DateTime _avisoAte;

    /// <summary>Processa roda do mouse; retorna true se rolou a timeline.</summary>
    public bool OnMouseWheel(int delta, Point p)
    {
        if (!IsOpen || !_viewport.Contains(p)) return false;
        int max = Math.Max(0, _contentHeight - _viewport.Height);
        _scroll = Math.Max(0, Math.Min(max, _scroll - delta));
        _host.Invalidate();
        return true;
    }

    private void EnsureInput()
    {
        if (_input is not null) return;
        _input = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.InputBackground,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10.5f),
            Multiline = false,
        };
        _input.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = SendAsync(); }
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
        };
        _host.Controls.Add(_input);
    }

    private async Task SendAsync()
    {
        if (_busy || _input is null || _session is null) return;
        var q = _input.Text.Trim();
        if (q.Length == 0) return;

        // A sessão registra o turno de user no próprio histórico; não duplicamos aqui.
        _input.Clear();
        _pendingError = null;
        _respostaParcial = null;
        _busy = true;
        _input.Enabled = false;
        _scrollToBottom = true;
        _host.Invalidate();
        try
        {
            await _session.SendStreamingAsync(q, MostrarParcial, _ct).ConfigureAwait(true);
            if (_host.IsDisposed) return;
        }
        catch (OperationCanceledException) { return; } // overlay fechado
        catch (Exception ex)
        {
            if (_host.IsDisposed) return;
            // Qualquer falha vira mensagem visível no chat (nunca engolida em silêncio).
            _pendingError = string.Format(CultureInfo.CurrentCulture, Strings.ChatError, ex.Message);
        }
        finally
        {
            if (!_host.IsDisposed)
            {
                // O texto definitivo já está no histórico da sessão (ou o turno
                // falhou); em qualquer caso o parcial deixa de valer. Mantê-lo
                // desenharia a resposta duas vezes.
                _respostaParcial = null;
                _busy = false;
                if (_input is not null) { _input.Enabled = true; _input.Focus(); }
                _scrollToBottom = true;
                _host.Invalidate();
            }
        }
    }

    /// <summary>
    /// Atualiza o balão da resposta em curso. Vem de uma thread de rede, então
    /// marshalla para a thread da UI antes de tocar em estado de desenho.
    /// </summary>
    private void MostrarParcial(string acumulado)
    {
        if (_host.IsDisposed) return;
        try
        {
            _host.BeginInvoke(() =>
            {
                if (_host.IsDisposed) return;
                // String vazia = o provedor principal falhou no meio e o fallback
                // vai recomeçar; o texto anterior não vale mais.
                _respostaParcial = acumulado.Length == 0 ? null : acumulado;
                _scrollToBottom = true;
                _host.Invalidate();
            });
        }
        catch (ObjectDisposedException) { } // overlay fechado no meio do fluxo
        catch (InvalidOperationException) { } // janela ainda sem handle
    }

    // ---------- Layout + render ----------
    public void Draw(Graphics g, Rectangle sel, Rectangle monitor, Rectangle sidePanel)
    {
        Layout(sel, monitor, sidePanel);
        Theme.DrawPanel(g, _bubble);
        var inner = Rectangle.Inflate(_bubble, -14, -14);

        // cabeçalho
        using (var hb = new SolidBrush(Theme.Text))
            g.DrawString(Icons.Sparkle, Icons.Cached(18), hb, inner.Left, inner.Top - 2);
        using (var tb = new SolidBrush(Theme.Text))
            g.DrawString(Strings.ChatTitle, HeaderFont, tb, inner.Left + 26, inner.Top);

        // Confirmação da cópia, alinhada à direita do cabeçalho e efêmera.
        if (_aviso is not null && DateTime.UtcNow < _avisoAte)
        {
            var tamanho = g.MeasureString(_aviso, MsgFont);
            using var tinta = new SolidBrush(Theme.TextMuted);
            g.DrawString(_aviso, MsgFont, tinta, inner.Right - tamanho.Width, inner.Top);
        }
        else if (_aviso is not null)
        {
            _aviso = null;
        }

        if (_aviso is null)
        {
            var reportSize = g.MeasureString(Strings.ChatReport, LinkFont);
            var privacySize = g.MeasureString(Strings.ChatPrivacy, LinkFont);
            _reportBtn = new Rectangle(
                inner.Right - (int)Math.Ceiling(reportSize.Width), inner.Top,
                (int)Math.Ceiling(reportSize.Width), 20);
            _privacyBtn = new Rectangle(
                _reportBtn.Left - 10 - (int)Math.Ceiling(privacySize.Width), inner.Top,
                (int)Math.Ceiling(privacySize.Width), 20);
            using var linkBrush = new SolidBrush(Theme.TextMuted);
            g.DrawString(Strings.ChatPrivacy, LinkFont, linkBrush, _privacyBtn.Location);
            g.DrawString(Strings.ChatReport, LinkFont, linkBrush, _reportBtn.Location);
        }
        else
        {
            _privacyBtn = Rectangle.Empty;
            _reportBtn = Rectangle.Empty;
        }

        using (var sep = new Pen(Theme.BorderSubtle, 1))
            g.DrawLine(sep, inner.Left, inner.Top + 26, inner.Right, inner.Top + 26);

        int inputH = 34;
        _viewport = new Rectangle(inner.Left, inner.Top + 34, inner.Width, inner.Bottom - inputH - 8 - (inner.Top + 34));

        var oldClip = g.Clip;
        g.SetClip(_viewport);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int maxBubbleW = (int)(_viewport.Width * 0.78);
        int y = _viewport.Top - _scroll;
        const int gap = 8, padX = 11, padY = 8;

        _copyButtons.Clear();

        // Desenha um balão (usuário à direita/claro, assistente à esquerda/escuro) e avança y.
        // O texto do assistente passa pelo separador de blocos, para que código
        // saia em fonte monoespaçada com fundo próprio.
        void DrawBubble(bool user, string text, bool copiavel)
        {
            int larguraTexto = maxBubbleW - padX * 2;
            var blocos = user
                ? new List<Block> { new(BlockKind.Paragraph, text) }
                : MarkdownBlocks.Parse(text);

            int alturaConteudo = MedirBlocos(g, blocos, larguraTexto);
            int bw = maxBubbleW;
            int bh = alturaConteudo + padY * 2;
            int bx = user ? _viewport.Right - bw : _viewport.Left;
            var bub = new Rectangle(bx, y, bw, bh);

            using (var p = Theme.RoundRect(bub, 10))
            using (var fill = new SolidBrush(user ? Color.White : Theme.SurfaceHover))
                g.FillPath(fill, p);

            DesenharBlocos(g, blocos, new Rectangle(bub.X + padX, bub.Y + padY, larguraTexto, alturaConteudo),
                user ? Color.Black : Theme.Text);

            // Botão de copiar: só nas respostas da IA, que é o que o usuário
            // quer levar embora. O texto é pixel desenhado — sem isto, não há
            // como selecioná-lo.
            if (copiavel && text.Length > 0)
            {
                var botao = new Rectangle(bub.Right - 26, bub.Top + 6, 20, 20);
                _copyButtons.Add((botao, text));
                using var tinta = new SolidBrush(Theme.TextMuted);
                g.DrawString(Icons.Copy, Icons.Cached(13), tinta, botao, CenterFmt);
            }

            y += bh + gap;
        }

        // Fonte única: histórico da sessão.
        var history = _session?.History;
        if (history is not null)
            foreach (var m in history)
            {
                bool ehUsuario = m.Role == "user";
                DrawBubble(ehUsuario, m.Text, copiavel: !ehUsuario);
            }

        // Resposta chegando por streaming, ainda fora do histórico. Sem botão de
        // copiar: copiar metade de uma resposta não serve para nada.
        if (_respostaParcial is not null)
            DrawBubble(false, _respostaParcial, copiavel: false);

        // Erro transitório do último turno (não faz parte do histórico da sessão).
        if (_pendingError is not null)
            DrawBubble(false, _pendingError, copiavel: false);

        // Reticências só até o primeiro pedaço chegar; depois o próprio texto
        // crescendo já mostra que a resposta está a caminho.
        if (_busy && _respostaParcial is null)
        {
            var bub = new Rectangle(_viewport.Left, y, 70, 30);
            using (var p = Theme.RoundRect(bub, 10))
            using (var fill = new SolidBrush(Theme.SurfaceHover))
                g.FillPath(fill, p);
            using (var tb = new SolidBrush(Theme.TextMuted))
                g.DrawString("• • •", MsgFont, tb, bub.X + padX, bub.Y + padY);
            y += 30 + gap;
        }

        _contentHeight = (y + _scroll) - _viewport.Top;

        if ((history is null || history.Count == 0) && _pendingError is null && !_busy)
        {
            using var ph = new SolidBrush(Theme.TextMuted);
            g.DrawString("Pergunte algo sobre o print…", MsgFont, ph, _viewport.Left, _viewport.Top + 4);
        }

        g.Clip = oldClip;

        if (_scrollToBottom)
        {
            _scroll = Math.Max(0, _contentHeight - _viewport.Height);
            _scrollToBottom = false;
        }

        // input + enviar
        var inputRow = new Rectangle(inner.Left, inner.Bottom - inputH, inner.Width, inputH);
        _sendBtn = new Rectangle(inputRow.Right - 32, inputRow.Top + 2, 30, 30);

        using (var ip = Theme.RoundRect(new Rectangle(inputRow.Left, inputRow.Top, inputRow.Width - 38, inputH), 9))
        using (var ifill = new SolidBrush(Theme.InputBackground))
        using (var ipen = new Pen(Theme.Border, 1))
        { g.FillPath(ifill, ip); g.DrawPath(ipen, ip); }

        if (_input is not null)
            _input.Bounds = new Rectangle(inputRow.Left + 10, inputRow.Top + 8, inputRow.Width - 38 - 18, 20);

        using (var p = Theme.RoundRect(_sendBtn, 9))
        using (var bb = new SolidBrush(_busy ? Theme.SurfaceHover : Color.White))
            g.FillPath(bb, p);
        using (var sb = new SolidBrush(_busy ? Theme.TextMuted : Color.Black))
            g.DrawString(Icons.Send, Icons.Cached(16), sb, _sendBtn, CenterFmt);
    }

    private static void OpenExternal(Uri uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch { /* o chat continua utilizavel se nao houver aplicativo associado */ }
    }

    // ---------- Desenho por blocos ----------

    /// <summary>Altura total que os blocos ocuparão na largura informada.</summary>
    private static int MedirBlocos(Graphics g, List<Block> blocos, int largura)
    {
        int total = 0;
        for (int i = 0; i < blocos.Count; i++)
        {
            total += MedirBloco(g, blocos[i], largura);
            if (i < blocos.Count - 1) total += 6; // respiro entre blocos
        }
        return Math.Max(total, (int)MsgFont.GetHeight(g));
    }

    private static int MedirBloco(Graphics g, Block bloco, int largura) => bloco.Kind switch
    {
        // O código não quebra linha: rola na horizontal se preciso, para não
        // desalinhar a indentação.
        BlockKind.Code => (int)Math.Ceiling(g.MeasureString(
            bloco.Text, CodeFont, int.MaxValue).Height) + 12,

        BlockKind.ListItem => (int)Math.Ceiling(g.MeasureString(
            MarkdownBlocks.StripInlineMarkup(bloco.Text), MsgFont, largura - RecuoDaLista).Height),

        _ => (int)Math.Ceiling(g.MeasureString(
            MarkdownBlocks.StripInlineMarkup(bloco.Text), MsgFont, largura).Height),
    };

    /// <summary>Desenha os blocos empilhados na área informada.</summary>
    private static void DesenharBlocos(Graphics g, List<Block> blocos, Rectangle area, Color corDoTexto)
    {
        int y = area.Top;

        foreach (var bloco in blocos)
        {
            int altura = MedirBloco(g, bloco, area.Width);

            switch (bloco.Kind)
            {
                case BlockKind.Code:
                    var fundo = new Rectangle(area.Left, y, area.Width, altura);
                    using (var p = Theme.RoundRect(fundo, 6))
                    using (var pincel = new SolidBrush(Theme.InputBackground))
                        g.FillPath(pincel, p);

                    // Recorta ao bloco: uma linha longa não pode invadir o
                    // balão seguinte.
                    var clipAnterior = g.Clip;
                    g.SetClip(fundo, System.Drawing.Drawing2D.CombineMode.Intersect);
                    using (var tinta = new SolidBrush(Theme.Text))
                        g.DrawString(bloco.Text, CodeFont, tinta, area.Left + 6, y + 6);
                    g.Clip = clipAnterior;
                    break;

                case BlockKind.ListItem:
                    using (var tinta = new SolidBrush(corDoTexto))
                    {
                        g.DrawString(bloco.Marker ?? "•", MsgFont, tinta, area.Left, y);
                        g.DrawString(
                            MarkdownBlocks.StripInlineMarkup(bloco.Text), MsgFont, tinta,
                            new RectangleF(area.Left + RecuoDaLista, y, area.Width - RecuoDaLista, altura));
                    }
                    break;

                default:
                    using (var tinta = new SolidBrush(corDoTexto))
                        g.DrawString(
                            MarkdownBlocks.StripInlineMarkup(bloco.Text), MsgFont, tinta,
                            new RectangleF(area.Left, y, area.Width, altura));
                    break;
            }

            y += altura + 6;
        }
    }

    /// <summary>
    /// Posiciona o balão: depois da marcação (direita→esquerda→abaixo→acima),
    /// sem cobrir o print; se não couber, sobrepõe sem tampar a toolbar lateral.
    /// </summary>
    private void Layout(Rectangle sel, Rectangle mon, Rectangle sidePanel)
    {
        const int w = 360, h = 340, m = 12;

        bool sideOnRight = !sidePanel.IsEmpty && sidePanel.Left >= sel.Right;
        bool sideOnLeft = !sidePanel.IsEmpty && sidePanel.Right <= sel.Left;
        int rightEdge = sideOnRight ? sidePanel.Right : sel.Right;
        int leftEdge = sideOnLeft ? sidePanel.Left : sel.Left;

        int yAligned = Math.Max(mon.Top + 8, Math.Min(sel.Top, mon.Bottom - h - 8));
        int xCentered = Math.Max(mon.Left + 8, Math.Min(sel.Left + (sel.Width - w) / 2, mon.Right - w - 8));

        var candidates = new[]
        {
            new Rectangle(rightEdge + m, yAligned, w, h),
            new Rectangle(leftEdge - m - w, yAligned, w, h),
            new Rectangle(xCentered, sel.Bottom + m, w, h),
            new Rectangle(xCentered, sel.Top - m - h, w, h),
        };

        foreach (var c in candidates)
        {
            bool insideMonitor = c.Left >= mon.Left + 4 && c.Top >= mon.Top + 4 &&
                                 c.Right <= mon.Right - 4 && c.Bottom <= mon.Bottom - 4;
            if (insideMonitor && !c.IntersectsWith(sel)) { _bubble = c; return; }
        }

        int fx = Math.Max(mon.Left + 8, Math.Min(sel.Left + 8, mon.Right - w - 8));
        int fy = Math.Max(mon.Top + 8, Math.Min(sel.Top + 8, mon.Bottom - h - 8));
        var cand = new Rectangle(fx, fy, w, h);
        if (!sidePanel.IsEmpty && cand.IntersectsWith(sidePanel))
        {
            bool sideRight = sidePanel.Left + sidePanel.Width / 2 >= mon.Left + mon.Width / 2;
            fx = sideRight ? sidePanel.Left - m - w : sidePanel.Right + m;
            fx = Math.Max(mon.Left + 8, Math.Min(fx, mon.Right - w - 8));
        }
        _bubble = new Rectangle(fx, fy, w, h);
    }
}
