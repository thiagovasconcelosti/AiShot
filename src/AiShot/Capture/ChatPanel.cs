using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AiShot.Ai;
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
    private static readonly StringFormat CenterFmt = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    private readonly Form _host;
    private readonly Func<IAiChatSession> _startSession;
    private readonly CancellationToken _ct;

    private IAiChatSession? _session;
    // Fonte única do diálogo é _session.History; aqui só guardamos um erro transitório
    // (a falha de um turno não entra no histórico da sessão, mas precisa aparecer no chat).
    private string? _pendingError;
    private bool _busy;
    private bool _scrollToBottom;
    private int _scroll, _contentHeight;
    private Rectangle _bubble, _viewport, _sendBtn;
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
        _session ??= _startSession(); // snapshot da imagem no momento da abertura
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
        return _bubble.Contains(p); // clique dentro do balão não vaza pro overlay
    }

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
        _busy = true;
        _input.Enabled = false;
        _scrollToBottom = true;
        _host.Invalidate();
        try
        {
            await _session.SendAsync(q, _ct).ConfigureAwait(true);
            if (_host.IsDisposed) return;
        }
        catch (OperationCanceledException) { return; } // overlay fechado
        catch (Exception ex)
        {
            if (_host.IsDisposed) return;
            // Qualquer falha vira mensagem visível no chat (nunca engolida em silêncio).
            _pendingError = "Erro: " + ex.Message;
        }
        finally
        {
            if (!_host.IsDisposed)
            {
                _busy = false;
                if (_input is not null) { _input.Enabled = true; _input.Focus(); }
                _scrollToBottom = true;
                _host.Invalidate();
            }
        }
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
            g.DrawString("Perguntar à IA", HeaderFont, tb, inner.Left + 26, inner.Top);
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

        // Desenha um balão (usuário à direita/claro, assistente à esquerda/escuro) e avança y.
        void DrawBubble(bool user, string text)
        {
            var sz = g.MeasureString(text, MsgFont, maxBubbleW - padX * 2);
            int bw = (int)Math.Ceiling(sz.Width) + padX * 2;
            int bh = (int)Math.Ceiling(sz.Height) + padY * 2;
            int bx = user ? _viewport.Right - bw : _viewport.Left;
            var bub = new Rectangle(bx, y, bw, bh);

            using (var p = Theme.RoundRect(bub, 10))
            using (var fill = new SolidBrush(user ? Color.White : Theme.SurfaceHover))
                g.FillPath(fill, p);
            using (var tb = new SolidBrush(user ? Color.Black : Theme.Text))
                g.DrawString(text, MsgFont, tb, new RectangleF(bub.X + padX, bub.Y + padY, maxBubbleW - padX * 2, bh));

            y += bh + gap;
        }

        // Fonte única: histórico da sessão.
        var history = _session?.History;
        if (history is not null)
            foreach (var m in history)
                DrawBubble(m.Role == "user", m.Text);

        // Erro transitório do último turno (não faz parte do histórico da sessão).
        if (_pendingError is not null)
            DrawBubble(false, _pendingError);

        if (_busy)
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
