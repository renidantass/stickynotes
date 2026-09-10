using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace StickyNotes.Services;

/// <summary>Descreve um monitor para a tela de configurações.</summary>
public sealed record MonitorInfo(string DeviceName, string DisplayName, Rect WorkArea, bool IsPrimary);

/// <summary>Enumera os monitores do sistema e resolve a workarea do monitor
/// selecionado em DIPs (unidades WPF), convertendo os pixels físicos do
/// monitor pela escala DPI do monitor primário.</summary>
public static class ScreenHelper
{
    /// <summary>Enumeração de monitores é P/Invoke caro por chamada (o App resolve o
    /// monitor a cada nota aberta/troca do deck) e o resultado só muda ao plugarem
    /// ou trocarem monitores: cacheia e invalida no DisplaySettingsChanged.
    /// Volátil: a invalidação pode vir da thread do SystemEvents.</summary>
    private static volatile IReadOnlyList<MonitorInfo>? _cache;

    static ScreenHelper()
    {
        SystemEvents.DisplaySettingsChanged += (_, _) => _cache = null;
    }

    /// <summary>Lista os monitores disponíveis, primário primeiro.</summary>
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        var monitors = new List<MonitorInfo>();
        var primaryDeviceName = GetPrimaryDeviceName();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = TryGetMonitorInfo(hMonitor);
            if (info.HasValue)
            {
                string deviceName = info.Value.szDevice;
                bool isPrimary = string.Equals(deviceName, primaryDeviceName, StringComparison.OrdinalIgnoreCase);
                var workArea = ToDips(info.Value.rcWork, hMonitor);
                monitors.Add(new MonitorInfo(
                    deviceName,
                    string.Empty, // preenchido depois (precisa do índice)
                    workArea,
                    isPrimary));
            }
            return true;
        }, IntPtr.Zero);

        // Ordena primário primeiro e monta o nome amigável.
        var ordered = monitors.OrderByDescending(m => m.IsPrimary).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var m = ordered[i];
            ordered[i] = m with
            {
                DisplayName = BuildDisplayName(m, i + 1),
            };
        }

        _cache = ordered;
        return _cache;
    }

    /// <summary>Resolve o monitor salvo (por DeviceName). Retorna o primário se o
    /// DeviceName não bater com nenhum monitor atual (ex.: monitor removido).</summary>
    public static MonitorInfo Resolve(string deviceName)
    {
        var monitors = GetMonitors();
        if (!string.IsNullOrEmpty(deviceName))
        {
            var match = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    private static string BuildDisplayName(MonitorInfo m, int index)
    {
        string primary = m.IsPrimary ? " (principal)" : "";
        return $"Monitor {index}{primary}";
    }

    /// <summary>Converte a workarea em pixels físicos para DIPs usando a escala DPI
    /// do próprio monitor — o WPF (system-DPI aware) mede a janela na escala do
    /// monitor onde ela está, então a workarea de cada monitor precisa vir na
    /// escala dele para o posicionamento bater.</summary>
    private static Rect ToDips(RECT pixels, IntPtr hMonitor)
    {
        double scale = GetMonitorDpiScale(hMonitor);
        return new Rect(
            pixels.left / scale,
            pixels.top / scale,
            (pixels.right - pixels.left) / scale,
            (pixels.bottom - pixels.top) / scale);
    }

    private static string? GetPrimaryDeviceName()
    {
        var primary = MonitorFromWindow(IntPtr.Zero, 1 /* MONITOR_DEFAULTTOPRIMARY */);
        return TryGetMonitorInfo(primary)?.szDevice;
    }

    private static double GetMonitorDpiScale(IntPtr hMonitor)
    {
        try
        {
            uint dpiX = 96, dpiY = 96;
            if (hMonitor != IntPtr.Zero &&
                GetDpiForMonitor(hMonitor, 0 /* MDT_EFFECTIVE_DPI */, out dpiX, out dpiY) == 0)
            {
                return dpiX / 96.0;
            }
        }
        catch
        {
            // fallback abaixo
        }
        return 1.0;
    }

    // --- P/Invoke (user32 / shcore) ---

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("shcore.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private static MONITORINFO? TryGetMonitorInfo(IntPtr hMonitor)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(hMonitor, ref info))
        {
            return info;
        }
        return null;
    }
}
