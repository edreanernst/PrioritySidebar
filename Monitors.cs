using System.Runtime.InteropServices;
using static PrioritySidebar.NativeMethods;

namespace PrioritySidebar;

internal sealed record MonitorInfo(IntPtr Handle, string DeviceName, RECT Bounds, bool IsPrimary)
{
    public uint Dpi => GetDpiForMonitor(Handle, MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi : 96;
}

internal static class Monitors
{
    /// <summary>All displays, left to right.</summary>
    public static List<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr hdc, ref RECT rect, IntPtr data) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(handle, ref info))
                list.Add(new MonitorInfo(handle, info.szDevice, info.rcMonitor, (info.dwFlags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return list.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    /// <summary>The display with this device name, or the main display if it is not connected.</summary>
    public static MonitorInfo Find(string? deviceName)
    {
        var all = All();
        return all.FirstOrDefault(m => m.DeviceName == deviceName)
            ?? all.FirstOrDefault(m => m.IsPrimary)
            ?? all.First();
    }

    public static string Describe(MonitorInfo monitor, int index)
    {
        var width = monitor.Bounds.Right - monitor.Bounds.Left;
        var height = monitor.Bounds.Bottom - monitor.Bounds.Top;
        var main = monitor.IsPrimary ? " (main)" : "";
        return $"Display {index + 1}{main}, {width} x {height}";
    }
}
