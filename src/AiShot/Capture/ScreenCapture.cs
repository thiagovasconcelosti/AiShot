using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace AiShot.Capture;

/// <summary>
/// Captura de região no estilo Lightshot/prntscr.
/// Tira um screenshot de toda a área virtual de telas (multi-monitor),
/// exibe um overlay fullscreen escurecido e deixa o usuário selecionar
/// um retângulo arrastando o mouse (rubber band).
/// </summary>
public sealed class ScreenCapture : IScreenCapture
{
    /// <inheritdoc/>
    public Bitmap? CaptureRegion()
    {
        // Limites de toda a área virtual (engloba todos os monitores).
        Rectangle virtualBounds = SystemInformation.VirtualScreen;

        // 1) Captura o fundo ANTES de mostrar qualquer overlay, para não capturá-lo.
        using var background = new Bitmap(virtualBounds.Width, virtualBounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(background))
        {
            g.CopyFromScreen(
                virtualBounds.Left, virtualBounds.Top,
                0, 0,
                virtualBounds.Size,
                CopyPixelOperation.SourceCopy);
        }

        // 2) Mostra o overlay e obtém a seleção do usuário (em coordenadas relativas ao VirtualScreen).
        using var overlay = new OverlayForm(background, virtualBounds);
        if (overlay.ShowDialog() != DialogResult.OK)
            return null; // Cancelado (Esc).

        Rectangle sel = overlay.SelectionInBitmap;

        // Clique sem arrastar (região vazia) => null.
        if (sel.Width <= 0 || sel.Height <= 0)
            return null;

        // 3) Recorta o fundo na região selecionada e devolve um novo Bitmap independente.
        var result = new Bitmap(sel.Width, sel.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.DrawImage(
                background,
                new Rectangle(0, 0, sel.Width, sel.Height),
                sel,
                GraphicsUnit.Pixel);
        }

        return result;
    }

    /// <summary>
    /// Form fullscreen sem borda usado como overlay de seleção.
    /// Desenha o screenshot escurecido e a região selecionada clareada com borda.
    /// </summary>
    private sealed class OverlayForm : Form
    {
        private readonly Bitmap _background;
        private readonly Rectangle _virtualBounds;

        private bool _selecting;
        private Point _startPoint;   // Ponto inicial do arrasto (coords do form/virtual screen).
        private Point _currentPoint; // Ponto atual do mouse.

        /// <summary>
        /// Região selecionada em coordenadas RELATIVAS ao bitmap de fundo
        /// (ou seja, relativas ao VirtualScreen). Vazia se nada foi selecionado.
        /// </summary>
        public Rectangle SelectionInBitmap { get; private set; } = Rectangle.Empty;

        public OverlayForm(Bitmap background, Rectangle virtualBounds)
        {
            _background = background;
            _virtualBounds = virtualBounds;

            // Configuração do form como overlay fullscreen.
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = virtualBounds;       // Cobre toda a área virtual.
            TopMost = true;
            ShowInTaskbar = false;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;        // Evita flicker do rubber band.
            KeyPreview = true;
            BackColor = Color.Black;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            _selecting = true;
            _startPoint = e.Location;
            _currentPoint = e.Location;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_selecting)
                return;

            _currentPoint = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !_selecting)
                return;

            _selecting = false;
            _currentPoint = e.Location;

            // Soltar o mouse confirma a seleção.
            Confirm();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Escape)
            {
                // Esc cancela e retorna null.
                DialogResult = DialogResult.Cancel;
                Close();
            }
            else if (e.KeyCode == Keys.Enter)
            {
                // Enter confirma a seleção atual.
                Confirm();
            }
        }

        /// <summary>Calcula a seleção, converte para coords do bitmap e fecha com OK.</summary>
        private void Confirm()
        {
            Rectangle selInClient = GetSelectionRectangle();

            // Converte de coords do client (form) para coords do bitmap de fundo.
            // O form começa em virtualBounds.Location, então o offset é igual a esse Location.
            SelectionInBitmap = new Rectangle(
                selInClient.X,
                selInClient.Y,
                selInClient.Width,
                selInClient.Height);

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Retângulo normalizado da seleção em coordenadas do client (form).</summary>
        private Rectangle GetSelectionRectangle()
        {
            int x = Math.Min(_startPoint.X, _currentPoint.X);
            int y = Math.Min(_startPoint.Y, _currentPoint.Y);
            int w = Math.Abs(_currentPoint.X - _startPoint.X);
            int h = Math.Abs(_currentPoint.Y - _startPoint.Y);
            return new Rectangle(x, y, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            // 1) Desenha o screenshot original cobrindo todo o form.
            g.DrawImage(_background, 0, 0, _background.Width, _background.Height);

            // 2) Overlay preto semitransparente (escurece tudo).
            using (var overlayBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                g.FillRectangle(overlayBrush, ClientRectangle);
            }

            if (!_selecting && SelectionInBitmap.IsEmpty)
                return;

            Rectangle sel = GetSelectionRectangle();
            if (sel.Width <= 0 || sel.Height <= 0)
                return;

            // 3) Redesenha SOMENTE a região selecionada com o screenshot original (sem escurecimento).
            g.DrawImage(
                _background,
                sel,                                  // destino (client)
                sel,                                  // origem (mesmo retângulo, pois offset client==bitmap)
                GraphicsUnit.Pixel);

            // 4) Borda fina ao redor da seleção.
            using (var borderPen = new Pen(Color.FromArgb(230, 30, 144, 255), 1f))
            {
                g.DrawRectangle(borderPen, sel.X, sel.Y, sel.Width, sel.Height);
            }

            // 5) Mostra as dimensões (LarguraxAltura) perto do cursor.
            DrawDimensions(g, sel);
        }

        /// <summary>Desenha o texto "WxH" próximo ao cursor atual.</summary>
        private void DrawDimensions(Graphics g, Rectangle sel)
        {
            string text = $"{sel.Width} x {sel.Height}";
            using var font = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Bold);
            SizeF textSize = g.MeasureString(text, font);

            // Posiciona o rótulo um pouco acima/à direita do cursor.
            float labelX = _currentPoint.X + 12;
            float labelY = _currentPoint.Y + 12;

            // Mantém o rótulo dentro dos limites visíveis.
            if (labelX + textSize.Width + 8 > ClientRectangle.Right)
                labelX = _currentPoint.X - textSize.Width - 12;
            if (labelY + textSize.Height + 4 > ClientRectangle.Bottom)
                labelY = _currentPoint.Y - textSize.Height - 12;

            var bgRect = new RectangleF(labelX - 4, labelY - 2, textSize.Width + 8, textSize.Height + 4);

            using (var bgBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            {
                g.FillRectangle(bgBrush, bgRect);
            }
            using (var textBrush = new SolidBrush(Color.White))
            {
                g.DrawString(text, font, textBrush, labelX, labelY);
            }
        }

        protected override void Dispose(bool disposing)
        {
            // O bitmap de fundo é de propriedade do chamador (using externo); não dispor aqui.
            base.Dispose(disposing);
        }
    }
}
