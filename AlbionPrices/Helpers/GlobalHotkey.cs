using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AlbionPrices.Helpers;

public class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int MOD_CONTROL = 0x0002;
    private const int VK_D = 0x44;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _window;
    private readonly int _hotkeyId;
    private HwndSource? _source;
    private bool _disposed;

    public event EventHandler? HotkeyPressed;

    public GlobalHotkey(Window window, int hotkeyId)
    {
        _window = window;
        _hotkeyId = hotkeyId;
    }

    public bool Register()
    {
        var helper = new WindowInteropHelper(_window);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(HwndHook);
        return RegisterHotKey(helper.Handle, _hotkeyId, MOD_CONTROL, VK_D);
    }

    public void Unregister()
    {
        var helper = new WindowInteropHelper(_window);
        UnregisterHotKey(helper.Handle, _hotkeyId);
        _source?.RemoveHook(HwndHook);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Unregister();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

public class TextExtractor
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr ChildWindowFromPoint(IntPtr hwndParent, POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_GETTEXTLENGTH = 0x000E;
    private const uint WM_COPY = 0x0301;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    public static string? GetTextUnderCursor()
    {
        try
        {
            GetCursorPos(out POINT cursorPos);
            var hwnd = WindowFromPoint(cursorPos);
            
            if (hwnd == IntPtr.Zero)
                return null;

            var childHwnd = ChildWindowFromPoint(hwnd, cursorPos);
            if (childHwnd != IntPtr.Zero)
                hwnd = childHwnd;

            int length = SendMessageTimeout(hwnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero, 0x0002, 1000, out _).ToInt32();
            
            if (length == 0)
                return null;

            IntPtr buffer = Marshal.AllocHGlobal((length + 1) * 2);
            try
            {
                SendMessage(hwnd, WM_GETTEXT, (IntPtr)(length + 1), buffer);
                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return null;
        }
    }
}