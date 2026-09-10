using System.Windows;
using System.Windows.Media;
using StickyNotes.Models;

namespace StickyNotes.ViewModels;

/// <summary>Material dos post-its: o papel é o artefato do produto, então ele
/// recebe o tratamento mais cuidado do app — gradiente com luz vinda de cima,
/// grão sutil, fita e canto dobrado. Tudo cacheado e congelado: a paleta é fixa
/// (6 cores), então nada é alocado durante o render.</summary>
public static class NoteColorBrush
{
    /// <summary>Cores da paleta das notas: post-it reconhecível, mas com a
    /// saturação puxada para baixo — papel de verdade não é fluorescente, e as
    /// notas competem com o chrome, então precisam de um tom mais assentado.</summary>
    private static readonly Dictionary<string, Color> Palette = new()
    {
        ["yellow"] = Color.FromRgb(0xFF, 0xE5, 0x8F),
        ["pink"] = Color.FromRgb(0xFF, 0xB3, 0xC7),
        ["blue"] = Color.FromRgb(0xA8, 0xCF, 0xFF),
        ["green"] = Color.FromRgb(0xB6, 0xE5, 0xA8),
        ["purple"] = Color.FromRgb(0xD9, 0xC2, 0xFF),
        ["orange"] = Color.FromRgb(0xFF, 0xD2, 0xA1),
    };

    private static readonly Dictionary<string, Brush> BrushCache = new();
    private static readonly Dictionary<string, LinearGradientBrush> GradientCache = new();
    private static readonly Dictionary<string, Brush> TapeCache = new();
    private static readonly Dictionary<string, Brush> FoldCache = new();
    private static readonly Dictionary<string, Brush> EdgeCache = new();

    /// <summary>Cor chapada do papel (usada onde o gradiente não aparece, como
    /// nas fatias finas da pill do deck).</summary>
    public static Brush Get(string color) =>
        GetOrAdd(BrushCache, color, static c => new SolidColorBrush(Resolve(c)));

    /// <summary>Gradiente do papel: realce no topo (luz de cima), corpo no meio
    /// e uma leve sombra na base. É o que dá volume à folha em vez de deixá-la
    /// um retângulo colorido.</summary>
    public static LinearGradientBrush GetGradient(string color) =>
        GetOrAdd(GradientCache, color, static c => BuildPaper(Resolve(c)));

    /// <summary>Fita translúcida sobre a nota — um único brush congelado,
    /// reutilizado a cada refresh.</summary>
    public static Brush GetTape() =>
        GetOrAdd(TapeCache, "tape", static _ => BuildTape());

    /// <summary>Canto dobrado da nota (cor escurecida), um brush congelado por cor.</summary>
    public static Brush GetFold(string color) =>
        GetOrAdd(FoldCache, color, static c => new SolidColorBrush(Darken(c)));

    /// <summary>Fio de cabelo que define a aresta do papel contra o fundo.
    /// Translúcido e derivado da própria cor: uma aresta preta neutra pareceria
    /// sujeira, e uma opaca seria forte demais.</summary>
    public static Brush GetEdge(string color) =>
        GetOrAdd(EdgeCache, color, static c =>
        {
            var d = Darken(c, 0.55);
            return new SolidColorBrush(Color.FromArgb(0x59, d.R, d.G, d.B));
        });

    /// <summary>Grão do papel: um padrão minúsculo e repetido, quase invisível,
    /// que tira o aspecto de plástico liso da superfície. Congelado e compartilhado.</summary>
    public static Brush GetGrain() => Grain;

    private static readonly Brush Grain = BuildGrain();

    /// <summary>Versão escurecida da cor (canto dobrado, aresta, sombras).</summary>
    public static Color Darken(string color, double factor = 0.72) => Darken(Resolve(color), factor);

    /// <summary>Versão aclarada na direção do branco (realce de topo).</summary>
    public static Color Lighten(string color, double amount) => Lighten(Resolve(color), amount);

    private static Color Darken(Color c, double factor) => Color.FromRgb(
        (byte)(c.R * factor),
        (byte)(c.G * factor),
        (byte)(c.B * factor));

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));

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
        if (created is Freezable freezable)
        {
            freezable.Freeze();
        }

        cache[color] = created;
        return created;
    }

    /// <summary>Papel: realce de topo, corpo e base levemente sombreada.</summary>
    private static LinearGradientBrush BuildPaper(Color c)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        brush.GradientStops.Add(new GradientStop(Lighten(c, 0.26), 0));
        brush.GradientStops.Add(new GradientStop(c, 0.42));
        brush.GradientStops.Add(new GradientStop(c, 0.78));
        brush.GradientStops.Add(new GradientStop(Darken(c, 0.90), 1));
        return brush;
    }

    /// <summary>Fita: translúcida com um leve gradiente, para não parecer um
    /// retângulo branco chapado.</summary>
    private static LinearGradientBrush BuildTape()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF), 1));
        return brush;
    }

    /// <summary>Padrão de grão construído uma única vez: um tile de 6x6 com
    /// alguns pontos de alfa ~3%. Repetido, lê como textura de papel.</summary>
    private static DrawingBrush BuildGrain()
    {
        var group = new DrawingGroup();
        group.Children.Add(Dot(0, 0, 0x0C));
        group.Children.Add(Dot(3, 2, 0x08));
        group.Children.Add(Dot(1, 4, 0x0A));
        group.Children.Add(Dot(4, 5, 0x07));

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 6, 6),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
            Opacity = 0.5,
        };
        return brush;

        static GeometryDrawing Dot(double x, double y, byte alpha) => new(
            new SolidColorBrush(Color.FromArgb(alpha, 0x00, 0x00, 0x00)),
            null,
            new RectangleGeometry(new Rect(x, y, 1, 1)));
    }
}
