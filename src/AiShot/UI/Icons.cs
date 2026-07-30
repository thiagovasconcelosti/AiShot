using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AiShot.UI;

/// <summary>
/// Carrega a fonte Phosphor (embutida) e expõe os glifos usados na UI.
/// Os codepoints vêm do style.css do @phosphor-icons/web (regular).
/// </summary>
public static class Icons
{
    private static readonly PrivateFontCollection _fonts = new();
    private static readonly FontFamily _family;

    static Icons()
    {
        var asm = Assembly.GetExecutingAssembly();
        // Nome do recurso embutido: <RootNamespace>.Assets.Phosphor.ttf
        var resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("Phosphor.ttf", StringComparison.OrdinalIgnoreCase));
        if (resName is not null)
        {
            using var s = asm.GetManifestResourceStream(resName)!;
            var data = new byte[s.Length];
            s.ReadExactly(data);
            var ptr = Marshal.AllocCoTaskMem(data.Length);
            Marshal.Copy(data, 0, ptr, data.Length);
            _fonts.AddMemoryFont(ptr, data.Length);
            Marshal.FreeCoTaskMem(ptr);
        }
        _family = _fonts.Families.Length > 0 ? _fonts.Families[0] : FontFamily.GenericSansSerif;
    }

    public static Font Font(float size) => new(_family, size, FontStyle.Regular, GraphicsUnit.Pixel);

    private static readonly Dictionary<int, Font> _cache = new();

    /// <summary>Fonte de ícone reaproveitada (cache por tamanho) — evita alocar no OnPaint.</summary>
    public static Font Cached(int size)
    {
        if (!_cache.TryGetValue(size, out var f))
        {
            f = Font(size);
            _cache[size] = f;
        }
        return f;
    }

    // Glifos (char a partir do codepoint hex do Phosphor)
    public static readonly string Pencil = G("e3b4");
    public static readonly string Arrow = G("e092");      // arrow-up-right
    public static readonly string Line = G("e6d2");        // line-segment
    public static readonly string Rectangle = G("e3f0");
    public static readonly string Circle = G("e18a");
    public static readonly string Text = G("e48a");        // text-t
    public static readonly string Palette = G("e6c8");
    public static readonly string Undo = G("e038");        // arrow-counter-clockwise
    public static readonly string Redo = G("e036");        // arrow-clockwise
    public static readonly string Cloud = G("e1ae");       // cloud-arrow-up
    public static readonly string Upload = G("e4c0");      // upload-simple
    public static readonly string Paint = G("e6f0");       // paint-brush
    public static readonly string Share = G("e408");       // share-network
    public static readonly string Copy = G("e1ca");
    public static readonly string Save = G("e248");        // floppy-disk
    public static readonly string Chat = G("e16c");        // chat-circle-dots
    public static readonly string Sparkle = G("e6a2");
    public static readonly string Close = G("e4f6");       // x
    public static readonly string Send = G("e396");        // paper-plane-right
    public static readonly string Spinner = Redo;          // mesmo glifo: arrow-clockwise

    private static string G(string hex) => char.ConvertFromUtf32(Convert.ToInt32(hex, 16));
}
