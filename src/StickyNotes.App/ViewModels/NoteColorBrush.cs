using System.Windows.Media;
using StickyNotes.Models;

namespace StickyNotes.ViewModels;

public static class NoteColorBrush
{
    /// <summary>Cores da paleta das notas (tema claro, estilo post-it).</summary>
    private static readonly Dictionary<string, Color> Palette = new()
    {
        ["yellow"] = Color.FromRgb(0xFF, 0xE8, 0x66),
        ["pink"] = Color.FromRgb(0xFF, 0x9F, 0xC6),
        ["blue"] = Color.FromRgb(0x8A, 0xC6, 0xFF),
        ["green"] = Color.FromRgb(0xA8, 0xE0, 0x9C),
        ["purple"] = Color.FromRgb(0xD6, 0xB8, 0xFF),
        ["orange"] = Color.FromRgb(0xFF, 0xD0, 0x7A),
    };

    // Brushes são caros de criar no WPF; a paleta é fixa (6 cores), então cacheia
    // os brushes/gradientes já prontos e reutiliza — evita alocação a cada
    // re-render do deck/mural (hover, digitação, troca de cor).
    private static readonly Dictionary<string, Brush> BrushCache = new();
    private static readonly Dictionary<string, LinearGradientBrush> GradientCache = new();
    private static readonly Dictionary<string, Brush> TapeCache = new();
    private static readonly Dictionary<string, Brush> FoldCache = new();

    public static Brush Get(string color) =>
        GetOrAdd(BrushCache, color, static c => new SolidColorBrush(Resolve(c)));

    /// <summary>Fita translúcida sobre a nota (idêntica para todas as cores) —
    /// um único brush congelado, reutilizado a cada refresh do preview.</summary>
    public static Brush GetTape() =>
        GetOrAdd(TapeCache, "tape", static _ => new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)));

    /// <summary>Canto dobrado da nota (cor escurecida), um brush congelado por cor.</summary>
    public static Brush GetFold(string color) =>
        GetOrAdd(FoldCache, color, static c => new SolidColorBrush(Darken(c)));

    /// <summary>Versão escurecida da cor (canto dobrado da nota, sombras).</summary>
    public static Color Darken(string color, double factor = 0.72)
    {
        var c = Resolve(color);
        return Color.FromRgb(
            (byte)(c.R * factor),
            (byte)(c.G * factor),
            (byte)(c.B * factor));
    }

    /// <summary>Gradiente vertical (topo mais claro, base mais escura) para dar
    /// profundidade de "pilha de post-its" às fatias do deck.</summary>
    public static LinearGradientBrush GetGradient(string color) =>
        GetOrAdd(GradientCache, color, static c => BuildGradient(Resolve(c)));

    public static string Next(string color)
    {
        int index = Array.IndexOf(NoteColors.All, color);
        return NoteColors.All[(index + 1) % NoteColors.All.Length];
    }

    private static Color Resolve(string color) =>
        Palette.TryGetValue(color, out var c) ? c : Palette[NoteColors.Default];

    private static T GetOrAdd<T>(Dictionary<string, T> cache, string color, Func<string, T> factory)
        where T : class
    {
        if (cache.TryGetValue(color, out var existing))
        {
            return existing;
        }

        var created = factory(color);
        // Congela: o brush é compartilhado e imutável — permite o WPF otimizar o
        // render (e garante que ninguém o modifique por engano).
        if (created is System.Windows.Freezable freezable)
        {
            freezable.Freeze();
        }
        cache[color] = created;
        return created;
    }

    private static LinearGradientBrush BuildGradient(Color c)
    {
        var light = Color.FromRgb(
            (byte)(c.R + (255 - c.R) * 0.18),
            (byte)(c.G + (255 - c.G) * 0.18),
            (byte)(c.B + (255 - c.B) * 0.18));
        var dark = Color.FromRgb(
            (byte)(c.R * 0.82),
            (byte)(c.G * 0.82),
            (byte)(c.B * 0.82));
        return new LinearGradientBrush(light, dark, 90);
    }
}
