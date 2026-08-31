using System.Windows;
using StickyNotes.Services;

namespace StickyNotes.Views;

/// <summary>Rotinas comuns das janelas do app: aplica o tema antes do XAML carregar
/// e esconde a janela do Alt+Tab/taskbar. Chamado no ctor e no OnSourceInitialized.</summary>
public static class AppWindowSetup
{
    public static void ApplyTheme(Window window)
    {
        ThemeManager.ApplyTo(window); // tema antes do XAML carregar (DynamicResource resolve certo)
    }

    public static void HideFromAltTab(Window window)
    {
        WindowHelper.HideFromAltTab(window);
    }
}
