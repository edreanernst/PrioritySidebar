using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using static PrioritySidebar.NativeMethods;

namespace PrioritySidebar;

/// <summary>
/// Registers a window as a Windows "app bar" on the right edge of a display, the same mechanism
/// the taskbar uses, so maximized and snapped windows stop at its edge instead of covering it.
/// </summary>
internal sealed class AppBar
{
    private readonly Window _window;
    private IntPtr _hwnd;
    private int _callbackMessage;
    private bool _registered;
    private bool _docking;
    private RECT _lastRect;

    public AppBar(Window window) => _window = window;

    public string? MonitorDevice { get; set; }
    public double WidthDip { get; set; } = 320;

    /// <summary>Raised with true when a full-screen app opens, false when it closes.</summary>
    public event Action<bool>? FullScreenChanged;

    /// <summary>Call from the window's SourceInitialized event.</summary>
    public void Attach()
    {
        _hwnd = new WindowInteropHelper(_window).Handle;

        // Tool window style keeps the sidebar out of Alt+Tab.
        var exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);

        HwndSource.FromHwnd(_hwnd).AddHook(WndProc);

        _callbackMessage = RegisterWindowMessage("PrioritySidebar.AppBarMessage");
        var data = NewData();
        data.uCallbackMessage = _callbackMessage;

        // Windows only forgets the strip of an app bar that died without saying goodbye (a crash or a
        // forced stop) when a live app bar is removed. Registering and removing once first clears it.
        SHAppBarMessage(ABM_NEW, ref data);
        SHAppBarMessage(ABM_REMOVE, ref data);

        SHAppBarMessage(ABM_NEW, ref data);
        _registered = true;

        Dock();
    }

    /// <summary>Reserves the strip and moves the window into it. Safe to call repeatedly.</summary>
    public void Dock()
    {
        if (!_registered || _docking) return;
        _docking = true;
        try
        {
            var monitor = Monitors.Find(MonitorDevice);
            var widthPx = (int)Math.Round(WidthDip * monitor.Dpi / 96.0);

            var data = NewData();
            data.uEdge = ABE_RIGHT;
            data.rc = new RECT
            {
                Left = monitor.Bounds.Right - widthPx,
                Top = monitor.Bounds.Top,
                Right = monitor.Bounds.Right,
                Bottom = monitor.Bounds.Bottom,
            };

            // Windows may trim the rectangle around other app bars; keep our width from its right edge.
            SHAppBarMessage(ABM_QUERYPOS, ref data);
            data.rc.Left = data.rc.Right - widthPx;

            // Skipping an unchanged position avoids a notification loop with other app bars.
            if (data.rc.Equals(_lastRect)) return;
            _lastRect = data.rc;

            SHAppBarMessage(ABM_SETPOS, ref data);
            SetWindowPos(_hwnd, IntPtr.Zero, data.rc.Left, data.rc.Top,
                data.rc.Right - data.rc.Left, data.rc.Bottom - data.rc.Top,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }
        finally
        {
            _docking = false;
        }
    }

    /// <summary>Forces a fresh placement, for example after the width or display setting changed.</summary>
    public void Redock()
    {
        _lastRect = default;
        Dock();
    }

    /// <summary>Gives the strip back to Windows. Call when the app exits.</summary>
    public void Remove()
    {
        if (!_registered) return;
        var data = NewData();
        SHAppBarMessage(ABM_REMOVE, ref data);
        _registered = false;
    }

    private APPBARDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<APPBARDATA>(),
        hWnd = _hwnd,
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_callbackMessage != 0 && msg == _callbackMessage)
        {
            switch (wParam.ToInt32())
            {
                case ABN_POSCHANGED:
                    _window.Dispatcher.BeginInvoke(Dock, DispatcherPriority.Background);
                    break;
                case ABN_FULLSCREENAPP:
                    FullScreenChanged?.Invoke(lParam != IntPtr.Zero);
                    break;
            }
        }
        else if (msg is WM_DISPLAYCHANGE or WM_DPICHANGED)
        {
            // Let WPF finish its own DPI handling first, then put the window back in its strip.
            _window.Dispatcher.BeginInvoke(Redock, DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }
}
