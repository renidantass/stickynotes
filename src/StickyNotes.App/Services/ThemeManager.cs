using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace StickyNotes.Services;

public enum AppTheme { Light, Dark }

/// <summary>Detecta o tema claro/escuro do Windows e expõe os recursos de cor
/// do app. A paleta é autoral nos dois temas: o escuro não é o claro invertido,
/// é um conjunto próprio de superfícies em degraus.
///
/// Os neutros são levemente tintados (quente) em vez de cinza puro — é o que faz
/// a superfície parecer escolhida em vez de vazia. Os valores de texto e o
/// vermelho vêm da escala de rótulos do sistema (Apple), que é um conjunto
/// já calibrado para contraste e hierarquia.</summary>
public static class ThemeManager
{
    private static AppTheme _current = AppTheme.Light;
    private static ThemePreference _preference = ThemePreference.System;

    /// <summary>Dispara quando o tema efetivo muda (preferência alterada ou sistema).</summary>
    public static event EventHandler? ThemeChanged;

    public static AppTheme Current => _current;

    public static ResourceDictionary LightResources { get; } = new();
    public static ResourceDictionary DarkResources { get; } = new();

    /// <summary>Paleta de Alto Contraste: mapeia os recursos do app para as cores do
    /// sistema, que o usuário escolhe justamente para ter contraste garantido.</summary>
    public static ResourceDictionary HighContrastResources { get; } = BuildHighContrast();

    static ThemeManager()
    {
        BuildLight();
        BuildDark();

        Detect();
        // Handler nomeado (não lambda): se um dia o ThemeManager precisar desassinar,
        // o lambda anônimo torna isso impossível (e vira leak de processo).
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        // Alto Contraste muda por SystemParameters, não por UserPreferenceChanged.
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    private static void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            Detect();
        }
    }

    // ---------------------------------------------------------------
    // Tema claro
    // ---------------------------------------------------------------

    private static void BuildLight()
    {
        var d = LightResources;

        // Superfícies: o fundo é um neutro quente; o papel dos post-its é que
        // traz a cor. O chrome nunca compete com o conteúdo.
        d["WallBrush"] = Brush("#F4F3F1");
        d["SurfaceBrush"] = Brush("#FFFFFF");
        d["SurfaceAltBrush"] = Brush("#FAF9F8");
        d["SurfaceElevatedBrush"] = Brush("#FFFFFF");
        // Materialidade do chrome: em vez de uma cor chapada, um gradiente
        // vertical sutil (luz vinda de cima) + realce de 1px na aresta superior.
        // É o que faz a superfície ler como material e não como retângulo cinza.
        d["ChromeFillBrush"] = Gradient("#FCFBFA", "#F0EFEC");
        d["ChromeHighlightBrush"] = Brush("#80FFFFFF");
        d["DeckBackgroundBrush"] = Gradient("#FBFAF8", "#F1F0ED");
        d["DeckBorderBrush"] = Brush("#14000000");

        // Tinta: três degraus, do conteúdo à metadata.
        d["TextPrimaryBrush"] = Brush("#1D1D1F");
        d["TextSecondaryBrush"] = Brush("#6E6E73");
        d["TextTertiaryBrush"] = Brush("#8E8E93");
        d["TextOnAccentBrush"] = Brush("#FFFFFF");
        d["TextOnInkBrush"] = Brush("#FFFFFF");

        // Bordas e separadores: fio de cabelo, sempre discreto.
        d["DividerBrush"] = Brush("#1A000000");
        d["SeparatorBrush"] = Brush("#14000000");
        d["ControlStrokeBrush"] = Brush("#1A000000");

        // Preenchimentos de controle
        d["ControlFillBrush"] = Brush("#0D000000");
        d["ControlFillHoverBrush"] = Brush("#14000000");
        d["ControlFillPressedBrush"] = Brush("#1F000000");
        d["SegmentedSelectedBrush"] = Brush("#1D1D1F");
        d["ScrollThumbBrush"] = Brush("#33000000");

        // Ação primária: tinta sólida. A cor do app já está nas notas.
        d["InkBrush"] = Brush("#1D1D1F");
        d["AccentBrush"] = Brush("#0A64D6");
        d["AccentSoftBrush"] = Brush("#1A0A64D6");
        d["FocusRingBrush"] = Brush("#0A64D6");
        d["DangerBrush"] = Brush("#D70015");
        d["DangerBackgroundBrush"] = Brush("#1AD70015");

        // Papel dos post-its: sempre claro, nos dois temas. Os tokens abaixo são
        // deliberadamente IGUAIS nos dois temas: eles pintam coisas que ficam
        // SOBRE o papel (a pílula de ações, o vinco), e o papel não muda com o
        // tema. Se seguissem o tema, a pílula ficaria branca sobre papel claro.
        d["NoteTextPrimaryBrush"] = Brush("#1F1F1F");
        d["NoteTextSecondaryBrush"] = Brush("#3A3A3C");
        d["NoteOverlayBrush"] = Brush("#B31F1F1F");
        d["NoteOverlayForegroundBrush"] = Brush("#FFFFFF");
        d["NoteOverlayDangerBrush"] = Brush("#FF8A80");
        d["TapeBrush"] = Brush("#66FFFFFF");
        d["FoldShadeBrush"] = Brush("#1F000000");

        d["BadgeBackgroundBrush"] = Brush("#1D1D1F");
        d["BadgeForegroundBrush"] = Brush("#FFFFFF");
        d["EmptyStateBrush"] = Brush("#A1A1A6");
    }

    // ---------------------------------------------------------------
    // Tema escuro
    // ---------------------------------------------------------------

    private static void BuildDark()
    {
        var d = DarkResources;

        // O escuro é autorado: cada superfície tem seu degrau próprio, do
        // fundo mais escuro ao material do chrome mais claro. É a luz
        // comunicando elevação, já que sombra quase não aparece no escuro.
        d["WallBrush"] = Brush("#1A1A1C");
        d["SurfaceBrush"] = Brush("#2C2C2E");
        d["SurfaceAltBrush"] = Brush("#242426");
        d["SurfaceElevatedBrush"] = Brush("#3A3A3C");
        d["ChromeFillBrush"] = Gradient("#37373A", "#2A2A2C");
        d["ChromeHighlightBrush"] = Brush("#1AFFFFFF");
        d["DeckBackgroundBrush"] = Gradient("#38383B", "#2B2B2D");
        d["DeckBorderBrush"] = Brush("#1FFFFFFF");

        d["TextPrimaryBrush"] = Brush("#F5F5F7");
        d["TextSecondaryBrush"] = Brush("#A1A1A6");
        d["TextTertiaryBrush"] = Brush("#8E8E93");
        d["TextOnAccentBrush"] = Brush("#FFFFFF");
        d["TextOnInkBrush"] = Brush("#1A1A1C");

        d["DividerBrush"] = Brush("#1FFFFFFF");
        d["SeparatorBrush"] = Brush("#1FFFFFFF");
        d["ControlStrokeBrush"] = Brush("#2EFFFFFF");

        d["ControlFillBrush"] = Brush("#14FFFFFF");
        d["ControlFillHoverBrush"] = Brush("#1FFFFFFF");
        d["ControlFillPressedBrush"] = Brush("#2EFFFFFF");
        d["SegmentedSelectedBrush"] = Brush("#F5F5F7");
        d["ScrollThumbBrush"] = Brush("#3DFFFFFF");

        d["InkBrush"] = Brush("#F5F5F7");
        d["AccentBrush"] = Brush("#5AA9FF");
        d["AccentSoftBrush"] = Brush("#1F5AA9FF");
        d["FocusRingBrush"] = Brush("#5AA9FF");
        d["DangerBrush"] = Brush("#FF453A");
        d["DangerBackgroundBrush"] = Brush("#33FF453A");

        // Texto sobre as notas: as notas são claras (post-its), então o texto é
        // SEMPRE escuro, mesmo no tema escuro — senão o contraste fica péssimo.
        d["NoteTextPrimaryBrush"] = Brush("#1F1F1F");
        d["NoteTextSecondaryBrush"] = Brush("#3A3A3C");
        d["NoteOverlayBrush"] = Brush("#B31F1F1F");
        d["NoteOverlayForegroundBrush"] = Brush("#FFFFFF");
        d["NoteOverlayDangerBrush"] = Brush("#FF8A80");
        d["TapeBrush"] = Brush("#3DFFFFFF");
        d["FoldShadeBrush"] = Brush("#1F000000");

        d["BadgeBackgroundBrush"] = Brush("#F5F5F7");
        d["BadgeForegroundBrush"] = Brush("#1A1A1C");
        d["EmptyStateBrush"] = Brush("#8E8E93");
    }

    /// <summary>Constrói a paleta de Alto Contraste a partir das cores do sistema.
    /// Os post-its mantêm a cor como conteúdo, mas o texto sobre eles força preto
    /// (as notas são claras) para continuar legível.</summary>
    private static ResourceDictionary BuildHighContrast()
    {
        return new ResourceDictionary
        {
            ["DeckBackgroundBrush"] = SystemColors.WindowBrush,
            ["DeckBorderBrush"] = SystemColors.WindowTextBrush,
            ["WallBrush"] = SystemColors.WindowBrush,
            ["SurfaceBrush"] = SystemColors.WindowBrush,
            ["SurfaceAltBrush"] = SystemColors.ControlBrush,
            ["SurfaceElevatedBrush"] = SystemColors.WindowBrush,
            ["ChromeFillBrush"] = SystemColors.WindowBrush,
            ["ChromeHighlightBrush"] = Brushes.Transparent,
            ["TextPrimaryBrush"] = SystemColors.WindowTextBrush,
            ["TextSecondaryBrush"] = SystemColors.WindowTextBrush,
            ["TextTertiaryBrush"] = SystemColors.GrayTextBrush,
            ["TextOnAccentBrush"] = SystemColors.HighlightTextBrush,
            ["TextOnInkBrush"] = SystemColors.HighlightTextBrush,
            ["DividerBrush"] = SystemColors.WindowTextBrush,
            ["SeparatorBrush"] = SystemColors.WindowTextBrush,
            ["ControlStrokeBrush"] = SystemColors.WindowTextBrush,
            ["ControlFillBrush"] = SystemColors.ControlBrush,
            ["ControlFillHoverBrush"] = SystemColors.HighlightBrush,
            ["ControlFillPressedBrush"] = SystemColors.HighlightBrush,
            ["SegmentedSelectedBrush"] = SystemColors.HighlightBrush,
            ["ScrollThumbBrush"] = SystemColors.WindowTextBrush,
            ["InkBrush"] = SystemColors.HighlightBrush,
            ["AccentBrush"] = SystemColors.HighlightBrush,
            ["AccentSoftBrush"] = SystemColors.ControlBrush,
            ["FocusRingBrush"] = SystemColors.HighlightBrush,
            ["DangerBrush"] = SystemColors.WindowTextBrush,
            ["DangerBackgroundBrush"] = SystemColors.ControlBrush,
            ["NoteTextPrimaryBrush"] = Brushes.Black,
            ["NoteTextSecondaryBrush"] = Brushes.Black,
            // Sobre o papel (sempre claro), o overlay precisa de contraste forte:
            // par texto/fundo do sistema, que o HC garante.
            ["NoteOverlayBrush"] = SystemColors.WindowTextBrush,
            ["NoteOverlayForegroundBrush"] = SystemColors.WindowBrush,
            ["NoteOverlayDangerBrush"] = SystemColors.WindowBrush,
            ["TapeBrush"] = Brushes.Transparent,
            ["FoldShadeBrush"] = SystemColors.WindowTextBrush,
            ["BadgeBackgroundBrush"] = SystemColors.HighlightBrush,
            ["BadgeForegroundBrush"] = SystemColors.HighlightTextBrush,
            ["EmptyStateBrush"] = SystemColors.GrayTextBrush,
        };
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
        {
            Detect();
        }
    }

    /// <summary>Define a preferência de tema (sistema/claro/escuro) e reaplica.</summary>
    public static void ApplyPreference(ThemePreference preference)
    {
        if (_preference == preference)
        {
            return;
        }

        _preference = preference;
        Detect();
    }

    private static void Detect()
    {
        _current = _preference switch
        {
            ThemePreference.Light => AppTheme.Light,
            ThemePreference.Dark => AppTheme.Dark,
            _ => DetectSystem(),
        };

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static AppTheme DetectSystem()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i)
            {
                return i == 1 ? AppTheme.Light : AppTheme.Dark;
            }
        }
        catch (System.Security.SecurityException)
        {
            // Sem acesso ao registro: mantém o tema claro.
        }
        catch (IOException)
        {
            // Registro indisponível momentaneamente: mantém o tema claro.
        }

        return AppTheme.Light;
    }

    /// <summary>Recursos de cor do tema atual. Em Alto Contraste usa a paleta do
    /// sistema: as cores fixas do app ignoram a preferência de contraste do usuário.</summary>
    public static ResourceDictionary Resources =>
        SystemParameters.HighContrast
            ? HighContrastResources
            : _current == AppTheme.Light ? LightResources : DarkResources;

    /// <summary>Aplica o dicionário de tema atual à janela e passa a reagir a mudanças
    /// de tema em runtime (a janela é re-temada quando o tema muda).</summary>
    public static void ApplyTo(Window window)
    {
        void Apply(object? _, EventArgs __)
        {
            window.Resources.MergedDictionaries.Clear();
            window.Resources.MergedDictionaries.Add(Resources);
        }

        ThemeChanged += Apply;
        window.Closed += (_, _) => ThemeChanged -= Apply;
        Apply(null, EventArgs.Empty);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush(Parse(hex));
        // Congelado: brush imutável e compartilhável — o WPF renderiza sem o custo
        // de verificação de mudança de cada brush dinâmico.
        brush.Freeze();
        return brush;
    }

    /// <summary>Gradiente vertical: topo mais claro, base mais escura. A luz
    /// sempre vem de cima, igual à direção das sombras do app.</summary>
    private static LinearGradientBrush Gradient(string top, string bottom)
    {
        var brush = new LinearGradientBrush(Parse(top), Parse(bottom), 90);
        brush.Freeze();
        return brush;
    }

    private static Color Parse(string hex) =>
        ColorConverter.ConvertFromString(hex) is Color c ? c : Colors.Transparent;
}
