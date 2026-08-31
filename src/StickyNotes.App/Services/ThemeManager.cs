using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace StickyNotes.Services;

public enum AppTheme { Light, Dark }

/// <summary>Detecta o tema claro/escuro do Windows e expõe os recursos de tema
/// (brushes WinUI/Fluent) para as janelas do app.</summary>
public static class ThemeManager
{
    private static AppTheme _current = AppTheme.Light;
    private static ThemePreference _preference = ThemePreference.System;

    /// <summary>Dispara quando o tema efetivo muda (preferência alterada ou sistema).</summary>
    public static event EventHandler? ThemeChanged;

    public static AppTheme Current => _current;

    public static ResourceDictionary LightResources { get; } = new();
    public static ResourceDictionary DarkResources { get; } = new();

    static ThemeManager()
    {
        // Brushes Fluent do Windows 11 — tema claro
        LightResources["DeckBackgroundBrush"] = Brush("#F3F3F3");
        LightResources["DeckBorderBrush"] = Brush("#00000014");
        LightResources["SurfaceBrush"] = Brush("#FFFFFF");
        LightResources["SurfaceAltBrush"] = Brush("#F3F3F3");
        LightResources["TextPrimaryBrush"] = Brush("#1A1A1A");
        LightResources["TextSecondaryBrush"] = Brush("#5D5D5D");
        LightResources["TextOnAccentBrush"] = Brush("#FFFFFF");
        LightResources["DividerBrush"] = Brush("#00000012");
        LightResources["ControlFillBrush"] = Brush("#0F000000");
        LightResources["ControlFillHoverBrush"] = Brush("#1A000000");
        LightResources["ControlFillPressedBrush"] = Brush("#26000000");
        LightResources["ControlStrokeBrush"] = Brush("#33000000");
        LightResources["AccentBrush"] = Brush("#005FB8");
        LightResources["DangerBrush"] = Brush("#C42B1C");
        LightResources["DangerBackgroundBrush"] = Brush("#1AC42B1C");
        LightResources["WallBrush"] = Brush("#F7F5F0");
        LightResources["NoteTextPrimaryBrush"] = Brush("#1F1F1F");
        LightResources["NoteTextSecondaryBrush"] = Brush("#2A2A2A");
        LightResources["TapeBrush"] = Brush("#66FFFFFF");
        LightResources["BadgeBackgroundBrush"] = Brush("#CC000000");
        LightResources["BadgeForegroundBrush"] = Brush("#FFFFFF");
        LightResources["FocusRingBrush"] = Brush("#005FB8");
        LightResources["EmptyStateBrush"] = Brush("#8A8578");

        // Brushes Fluent do Windows 11 — tema escuro
        // Superfícies separadas em degraus: deck (mais claro) > mural > superfície de ações.
        DarkResources["DeckBackgroundBrush"] = Brush("#3A3A3A");
        DarkResources["DeckBorderBrush"] = Brush("#4DFFFFFF");
        DarkResources["SurfaceBrush"] = Brush("#2B2B2B");
        DarkResources["SurfaceAltBrush"] = Brush("#222222");
        DarkResources["TextPrimaryBrush"] = Brush("#FFFFFF");
        DarkResources["TextSecondaryBrush"] = Brush("#C5C5C5");
        DarkResources["TextOnAccentBrush"] = Brush("#FFFFFF");
        DarkResources["DividerBrush"] = Brush("#1FFFFFFF");
        DarkResources["ControlFillBrush"] = Brush("#0FFFFFFF");
        DarkResources["ControlFillHoverBrush"] = Brush("#1AFFFFFF");
        DarkResources["ControlFillPressedBrush"] = Brush("#26FFFFFF");
        DarkResources["ControlStrokeBrush"] = Brush("#4DFFFFFF");
        DarkResources["AccentBrush"] = Brush("#60CDFF");
        DarkResources["DangerBrush"] = Brush("#FF99A4");
        DarkResources["DangerBackgroundBrush"] = Brush("#33FF99A4");
        DarkResources["WallBrush"] = Brush("#232323");
        // Texto sobre as notas: as notas são claras (post-its), então o texto é SEMPRE escuro,
        // mesmo no tema escuro — senão o contraste fica péssimo.
        DarkResources["NoteTextPrimaryBrush"] = Brush("#1F1F1F");
        DarkResources["NoteTextSecondaryBrush"] = Brush("#2A2A2A");
        DarkResources["TapeBrush"] = Brush("#3DFFFFFF");
        DarkResources["BadgeBackgroundBrush"] = Brush("#E6E6E6");
        DarkResources["BadgeForegroundBrush"] = Brush("#1A1A1A");
        DarkResources["FocusRingBrush"] = Brush("#60CDFF");
        DarkResources["EmptyStateBrush"] = Brush("#8A8A8A");

        Detect();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
            {
                Detect();
            }
        };
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

    /// <summary>Recursos de brush do tema atual.</summary>
    public static ResourceDictionary Resources =>
        _current == AppTheme.Light ? LightResources : DarkResources;

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

    private static System.Windows.Media.SolidColorBrush Brush(string hex) =>
        new(System.Windows.Media.ColorConverter.ConvertFromString(hex) is System.Windows.Media.Color c ? c : System.Windows.Media.Colors.Transparent);
}
