using System.IO;
using Microsoft.Win32;

namespace StickyNotes.Services;

/// <summary>Centraliza o sistema de motion do app e respeita o "reduced motion"
/// do Windows (Configurações > Acessibilidade > Efeitos de animação).</summary>
public static class MotionService
{
    private static readonly bool _enabled = Detect();

    /// <summary>Falso quando o usuário pediu menos animação no Windows.</summary>
    public static bool Enabled => _enabled;

    private static bool Detect()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Accessibility");
            var value = key?.GetValue("ClientAreaAnimation");
            // 0 = animações desligadas
            return value is int i ? i == 1 : true;
        }
        catch (System.Security.SecurityException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
