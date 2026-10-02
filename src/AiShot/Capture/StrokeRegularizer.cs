using System.Drawing;

namespace AiShot.Capture;

/// <summary>
/// Forma regular reconhecida num traço à mão: a ferramenta e os dois pontos que
/// a definem — o mesmo par A/B das formas desenhadas com o mouse.
/// </summary>
internal readonly record struct RegularStroke(Tool Tool, Point A, Point B);

/// <summary>
/// Endireita traços: reconhece um traço à mão como linha, círculo ou retângulo e
/// aplica as restrições do Shift enquanto a forma é desenhada.
/// </summary>
/// <remarks>
/// <para>
/// Funções puras sobre pontos: quem decide quando aplicar é o overlay. O
/// reconhecimento é conservador de propósito — na dúvida o traço continua à mão,
/// porque transformar um rabisco em retângulo é pior do que não transformar.
/// </para>
/// <para>
/// A ordem dos testes é o que separa as formas. O retângulo vem primeiro porque é
/// o mais específico: exige quase todos os pontos encostados nas bordas e as
/// quatro bordas cobertas, o que um círculo não cumpre (os pontos a 45° ficam no
/// miolo). Depois o círculo, com o centro vindo de um ajuste por mínimos
/// quadrados — o centro da caixa erra quando o traço não fecha no mesmo ponto, que
/// é o caso normal de quem desenha à mão — e exigindo que o traço dê a volta.
/// </para>
/// </remarks>
internal static class StrokeRegularizer
{
    /// <summary>Pontos mínimos para tentar reconhecer alguma coisa.</summary>
    public const int MinPoints = 8;

    /// <summary>Comprimento mínimo do traço, em pixels.</summary>
    public const int MinLength = 40;

    /// <summary>Lado mínimo de um círculo ou retângulo reconhecido.</summary>
    public const int MinSide = 16;

    // Folgas do reconhecimento, em fração: da distância entre as pontas (traço
    // fechado), do raio ajustado (círculo) e do menor lado da caixa (retângulo).
    private const double ToleranceClosed = 0.35;
    private const double ToleranceRadius = 0.25;
    private const double ToleranceEdge = 0.14;

    /// <summary>Fração dos pontos que precisa estar encostada nas bordas.</summary>
    private const double EdgeCoverage = 0.95;

    /// <summary>Fração do giro que o traço precisa cobrir para virar círculo.</summary>
    private const double CoverageMin = 0.85;

    /// <summary>Fração mínima do comprimento da corda para valer como linha.</summary>
    private const double ToleranceLine = 0.10;

    /// <summary>
    /// Restringe o ponto de arraste à forma regular da ferramenta: reta em
    /// 0/45/90/135 graus, e quadrado para retângulo, elipse e seta — o círculo é
    /// a elipse de caixa quadrada.
    /// </summary>
    public static Point Restringir(Tool ferramenta, Point ancora, Point atual)
    {
        int dx = atual.X - ancora.X, dy = atual.Y - ancora.Y;

        if (ferramenta is Tool.Rect or Tool.Ellipse)
        {
            // O maior lado manda, e o sinal preserva o quadrante em que o usuário
            // está arrastando — com zero, o lado positivo, para o quadrado não
            // colapsar quando o arraste é perfeitamente reto.
            int lado = Math.Max(Math.Abs(dx), Math.Abs(dy));
            return new Point(
                ancora.X + (dx < 0 ? -lado : lado),
                ancora.Y + (dy < 0 ? -lado : lado));
        }

        if (ferramenta is not (Tool.Line or Tool.Arrow)) return atual;

        // Reta: trava a direção no múltiplo de 45° mais próximo e mantém o quanto
        // o mouse andou nessa direção (projeção), para o comprimento acompanhar o
        // gesto em vez de saltar.
        double rad = Math.Atan2(dy, dx);
        double travada = Math.Round(rad / (Math.PI / 4)) * (Math.PI / 4);
        double comprimento = dx * Math.Cos(travada) + dy * Math.Sin(travada);

        return new Point(
            ancora.X + (int)Math.Round(Math.Cos(travada) * comprimento),
            ancora.Y + (int)Math.Round(Math.Sin(travada) * comprimento));
    }

    /// <summary>
    /// Reconhece o traço como linha, círculo ou retângulo regular. Devolve nulo
    /// quando não é nenhum dos três — um rabisco continua um rabisco.
    /// </summary>
    public static RegularStroke? Reconhecer(IReadOnlyList<Point>? traco)
    {
        if (traco is null || traco.Count < MinPoints) return null;
        if (Comprimento(traco) < MinLength) return null;

        var caixa = Caixa(traco);

        // Retângulo primeiro: é o teste mais específico e não depende de fechar,
        // porque exige as quatro bordas cobertas.
        if (caixa.Width >= MinSide && caixa.Height >= MinSide && EhRetangulo(traco, caixa))
            return new RegularStroke(Tool.Rect, caixa.Location, new Point(caixa.Right, caixa.Bottom));

        // Círculo: centro pelo ajuste, e o traço precisa dar a volta. Quem desenha
        // à mão quase nunca fecha no mesmo ponto — exigir que as pontas se
        // encontrem deixaria o círculo de fora.
        if (AjustarCirculo(traco, out var centro, out var raio)
            && raio >= MinSide / 2.0
            && Dentro(caixa, centro)
            && Cobertura(traco, centro) >= CoverageMin
            && EhCirculo(traco, centro, raio))
        {
            int r = (int)Math.Round(raio);
            int cx = (int)Math.Round(centro.X), cy = (int)Math.Round(centro.Y);
            return new RegularStroke(Tool.Ellipse, new Point(cx - r, cy - r), new Point(cx + r, cy + r));
        }

        // Aberto e reto: linha.
        return !Fechado(traco, caixa) && EhReta(traco, traco[0], traco[^1])
            ? new RegularStroke(Tool.Line, traco[0], traco[^1])
            : null;
    }

    /// <summary>As pontas do traço se encontram (com a folga de quem desenha à mão)?</summary>
    private static bool Fechado(IReadOnlyList<Point> traco, Rectangle caixa) =>
        Distancia(traco[0], traco[^1]) <= ToleranceClosed * Math.Max(caixa.Width, caixa.Height);

    private static bool EhReta(IReadOnlyList<Point> traco, Point a, Point b)
    {
        double comprimento = Distancia(a, b);
        if (comprimento < MinLength) return false;

        double tolerancia = ToleranceLine * comprimento;
        foreach (var p in traco)
            if (DistanciaAoSegmento(p, a, b) > tolerancia) return false;

        return true;
    }

    private static bool EhCirculo(IReadOnlyList<Point> traco, PointF centro, double raio)
    {
        double tolerancia = ToleranceRadius * raio;
        foreach (var p in traco)
            if (Math.Abs(Distancia(p, centro) - raio) > tolerancia) return false;

        return true;
    }

    private static bool EhRetangulo(IReadOnlyList<Point> traco, Rectangle caixa)
    {
        double tolerancia = ToleranceEdge * Math.Min(caixa.Width, caixa.Height);

        int encostados = 0, esquerda = 0, direita = 0, topo = 0, base_ = 0;
        foreach (var p in traco)
        {
            double dEsquerda = p.X - caixa.Left;
            double dDireita = caixa.Right - p.X;
            double dTopo = p.Y - caixa.Top;
            double dBase = caixa.Bottom - p.Y;

            if (Math.Min(Math.Min(dEsquerda, dDireita), Math.Min(dTopo, dBase)) > tolerancia) continue;

            encostados++;
            if (dEsquerda <= tolerancia) esquerda++;
            if (dDireita <= tolerancia) direita++;
            if (dTopo <= tolerancia) topo++;
            if (dBase <= tolerancia) base_++;
        }

        // O contorno tem de passar pelas quatro bordas: um arco em forma de
        // meia-lua encosta em duas e não é retângulo.
        int minimo = Math.Max(2, traco.Count / 10);
        return encostados >= EdgeCoverage * traco.Count
            && esquerda >= minimo && direita >= minimo && topo >= minimo && base_ >= minimo;
    }

    /// <summary>
    /// Ajuste de círculo por mínimos quadrados (Kåsa): devolve o centro e o raio
    /// que melhor passam pelos pontos.
    /// </summary>
    /// <remarks>
    /// Os pontos entram centrados na própria média: com coordenadas grandes — a
    /// área virtual pode ter origem negativa — o sistema ficaria mal condicionado.
    /// Pontos colineares (uma reta) deixam o determinante em zero: não há círculo.
    /// </remarks>
    private static bool AjustarCirculo(IReadOnlyList<Point> traco, out PointF centro, out double raio)
    {
        centro = default;
        raio = 0;
        int n = traco.Count;
        if (n < 3) return false;

        double mediaX = 0, mediaY = 0;
        foreach (var p in traco) { mediaX += p.X; mediaY += p.Y; }
        mediaX /= n; mediaY /= n;

        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, sxz = 0, syz = 0, sz = 0;
        foreach (var p in traco)
        {
            double x = p.X - mediaX, y = p.Y - mediaY;
            double z = x * x + y * y;
            sx += x; sy += y;
            sxx += x * x; syy += y * y; sxy += x * y;
            sxz += x * z; syz += y * z; sz += z;
        }

        // Sistema 3x3 em D, E e F:
        //   sxx·D + sxy·E + sx·F = -sxz
        //   sxy·D + syy·E + sy·F = -syz
        //    sx·D +  sy·E +  n·F = -sz
        double det = Det(sxx, sxy, sx, sxy, syy, sy, sx, sy, n);
        if (Math.Abs(det) < 1e-6) return false; // pontos colineares: não há círculo

        double d = Det(-sxz, sxy, sx, -syz, syy, sy, -sz, sy, n) / det;
        double e = Det(sxx, -sxz, sx, sxy, -syz, sy, sx, -sz, n) / det;
        double f = Det(sxx, sxy, -sxz, sxy, syy, -syz, sx, sy, -sz) / det;

        double cx = -d / 2, cy = -e / 2;
        double quadrado = cx * cx + cy * cy - f;
        if (quadrado <= 0) return false;

        centro = new PointF((float)(cx + mediaX), (float)(cy + mediaY));
        raio = Math.Sqrt(quadrado);
        return true;
    }

    /// <summary>Determinante de uma matriz 3x3 montada por linhas (regra de Cramer).</summary>
    private static double Det(
        double a11, double a12, double a13,
        double a21, double a22, double a23,
        double a31, double a32, double a33) =>
        a11 * (a22 * a33 - a23 * a32)
        - a12 * (a21 * a33 - a23 * a31)
        + a13 * (a21 * a32 - a22 * a31);

    /// <summary>Fração do giro em torno do centro que o traço cobre (0 a 1).</summary>
    /// <remarks>
    /// Marca o setor de cada ponto <b>e</b> os setores entre pontos consecutivos:
    /// olhar só os pontos deixaria buracos nos lugares que o traço atravessou — um
    /// círculo desenhado rápido tem poucos pontos e passaria a ser recusado.
    /// </remarks>
    private static double Cobertura(IReadOnlyList<Point> traco, PointF centro)
    {
        const int setores = 36;
        var ocupados = new bool[setores];

        void Marcar(double angulo)
        {
            int setor = (int)Math.Floor((angulo + Math.PI) / (2 * Math.PI) * setores);
            ocupados[Math.Clamp(setor, 0, setores - 1)] = true;
        }

        double anterior = Angulo(traco[0], centro);
        Marcar(anterior);

        for (int i = 1; i < traco.Count; i++)
        {
            double atual = Angulo(traco[i], centro);

            // O passo entre pontos vizinhos é pequeno, mas pode cruzar o corte de
            // -π/π: normaliza para [-π, π] antes de interpolar.
            double passo = atual - anterior;
            while (passo > Math.PI) passo -= 2 * Math.PI;
            while (passo < -Math.PI) passo += 2 * Math.PI;

            int divisoes = Math.Max(1, (int)Math.Ceiling(Math.Abs(passo) / (2 * Math.PI) * setores));
            for (int d = 0; d <= divisoes; d++) Marcar(anterior + passo * d / divisoes);

            anterior = atual;
        }

        int total = 0;
        foreach (var o in ocupados) if (o) total++;
        return (double)total / setores;
    }

    private static double Angulo(Point p, PointF centro) => Math.Atan2(p.Y - centro.Y, p.X - centro.X);

    /// <summary>O centro ajustado cai dentro da caixa do traço (com folga)?</summary>
    private static bool Dentro(Rectangle caixa, PointF centro)
    {
        int folga = Math.Max(4, (int)Math.Round(0.20 * Math.Min(caixa.Width, caixa.Height)));
        return Rectangle.Inflate(caixa, folga, folga)
            .Contains((int)Math.Round(centro.X), (int)Math.Round(centro.Y));
    }

    private static Rectangle Caixa(IReadOnlyList<Point> traco)
    {
        int minX = traco[0].X, maxX = traco[0].X, minY = traco[0].Y, maxY = traco[0].Y;
        foreach (var p in traco)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }

        return Rectangle.FromLTRB(minX, minY, maxX, maxY);
    }

    private static double Comprimento(IReadOnlyList<Point> traco)
    {
        double total = 0;
        for (int i = 1; i < traco.Count; i++) total += Distancia(traco[i - 1], traco[i]);
        return total;
    }

    private static double Distancia(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Distancia(Point p, PointF c)
    {
        double dx = p.X - c.X, dy = p.Y - c.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Distância do ponto ao segmento (não à reta infinita).</summary>
    private static double DistanciaAoSegmento(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double comprimento2 = dx * dx + dy * dy;
        if (comprimento2 <= 0) return Distancia(p, a);

        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / comprimento2, 0, 1);
        double pertoX = a.X + t * dx, pertoY = a.Y + t * dy;
        double ex = p.X - pertoX, ey = p.Y - pertoY;
        return Math.Sqrt(ex * ex + ey * ey);
    }
}
