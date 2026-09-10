using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StickyNotes.Services;

/// <summary>Ajustes de janela Win32 que o WPF não expõe diretamente.</summary>
public static class WindowHelper
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;

    // As variantes "Ptr" são as corretas em processo x64/arm64 (o estilo de janela
    // é um LONG_PTR); as de 32 bits existem apenas para o caso de um build de 32 bits.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>Aplica WS_EX_TOOLWINDOW: a janela não aparece no Alt+Tab nem na taskbar.</summary>
    public static void HideFromAltTab(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        long style = GetExtendedStyle(handle);
        if ((style & WsExToolWindow) == 0)
        {
            SetExtendedStyle(handle, style | WsExToolWindow);
        }
    }

    private static long GetExtendedStyle(IntPtr handle) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr(handle, GwlExStyle).ToInt64()
            : GetWindowLong32(handle, GwlExStyle);

    private static void SetExtendedStyle(IntPtr handle, long style)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
        }
        else
        {
            SetWindowLong32(handle, GwlExStyle, (int)style);
        }
    }
}
