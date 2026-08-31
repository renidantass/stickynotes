using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StickyNotes.Services;

/// <summary>Ajustes de janela Win32 que o WPF não expõe diretamente.</summary>
public static class WindowHelper
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern long GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern long SetWindowLong(IntPtr hWnd, int nIndex, long dwNewLong);

    /// <summary>Aplica WS_EX_TOOLWINDOW: a janela não aparece no Alt+Tab nem na taskbar.</summary>
    public static void HideFromAltTab(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        long style = GetWindowLong(handle, GwlExStyle);
        if ((style & WsExToolWindow) == 0)
        {
            SetWindowLong(handle, GwlExStyle, style | WsExToolWindow);
        }
    }
}
