using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CafeSoftware.Services;

// Covers secondary monitors and keeps the lock screen in front
public sealed class LockGuard : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private readonly Window _lock;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<(Window Win, RECT Rect)> _covers = new();
    private string _signature = "";
    private bool _allowClose;
    private bool _enabled;

    public LockGuard(Window lockWindow)
    {
        _lock = lockWindow;
        _timer.Tick += (_, _) => Tick();
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value)
            {
                _signature = "";
                Tick();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                CloseCovers();
            }
        }
    }

    private void Tick()
    {
        if (!_enabled || App.AdminOpen) return;

        // Rebuild the covers if monitors were added, removed or moved
        var others = GetSecondaryMonitors();
        string sig = string.Join("|", others.Select(r => $"{r.Left},{r.Top},{r.Right},{r.Bottom}"));
        if (sig != _signature)
        {
            CloseCovers();
            foreach (var r in others) AddCover(r);
            _signature = sig;
        }

        // Put covers back on top
        foreach (var (win, rect) in _covers) Place(win, rect);

        // Keep the lock screen in front of everything else
        if (_lock.IsVisible && !_lock.IsActive)
        {
            _lock.Topmost = false;
            _lock.Topmost = true;
            _lock.Activate();
        }
    }

    private static List<RECT> GetSecondaryMonitors()
    {
        var list = new List<RECT>();
        MonitorEnumProc callback = (IntPtr h, IntPtr dc, ref RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(h, ref mi) && (mi.dwFlags & 1) == 0)   // 1 = primary monitor
                list.Add(mi.rcMonitor);
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return list;
    }

    private void AddCover(RECT r)
    {
        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Left = r.Left,
            Top = r.Top,
            Width = 300,
            Height = 200,
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x18, 0x20)),
            Content = new System.Windows.Controls.TextBlock
            {
                Text = App.Config.ShopName,
                FontSize = 48,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4B, 0x5D, 0x70)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        win.Closing += (_, e) => { if (!_allowClose) e.Cancel = true; };
        win.Show();
        _covers.Add((win, r));
        Place(win, r);
    }

    private static void Place(Window win, RECT r)
    {
        IntPtr hwnd = new WindowInteropHelper(win).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HWND_TOPMOST, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                     SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private void CloseCovers()
    {
        _allowClose = true;
        foreach (var (win, _) in _covers)
        {
            try { win.Close(); } catch { }
        }
        _covers.Clear();
        _allowClose = false;
        _signature = "";
    }

    public void Dispose()
    {
        _timer.Stop();
        CloseCovers();
    }
}