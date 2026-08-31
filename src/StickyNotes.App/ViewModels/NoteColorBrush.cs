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

    public static Brush Get(string color) =>
        new SolidColorBrush(Palette.TryGetValue(color, out var c) ? c : Palette[NoteColors.Default]);

    /// <summary>Versão escurecida da cor (canto dobrado da nota, sombras).</summary>
    public static Color Darken(string color, double factor = 0.72)
    {
        var c = Palette.TryGetValue(color, out var value) ? value : Palette[NoteColors.Default];
        return Color.FromRgb(
            (byte)(c.R * factor),
            (byte)(c.G * factor),
            (byte)(c.B * factor));
    }

    /// <summary>Gradiente vertical (topo mais claro, base mais escura) para dar
    /// profundidade de "pilha de post-its" às fatias do deck.</summary>
    public static LinearGradientBrush GetGradient(string color)
    {
        var c = Palette.TryGetValue(color, out var value) ? value : Palette[NoteColors.Default];
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

    public static string Next(string color)
    {
        int index = Array.IndexOf(NoteColors.All, color);
        return NoteColors.All[(index + 1) % NoteColors.All.Length];
    }
}
