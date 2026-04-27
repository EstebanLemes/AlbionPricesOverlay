#if WINDOWS
using System.Runtime.InteropServices;
using System.Threading;

namespace AlbionPrices.Helpers;

public class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY   = 0x0312;
    private const int MOD_CONTROL = 0x0002;
    private const int VK_D        = 0x44;

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern bool GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName,
        int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd; public uint message;
        public IntPtr wParam; public IntPtr lParam;
        public uint time; public int ptX, ptY;
    }

    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly int _hotkeyId;
    private IntPtr       _hwnd;
    private bool         _disposed;

    public event EventHandler? HotkeyPressed;

    public GlobalHotkey(int hotkeyId) => _hotkeyId = hotkeyId;

    public bool Register()
    {
        var registered = false;
        var ready      = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            _hwnd      = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            registered = RegisterHotKey(_hwnd, _hotkeyId, MOD_CONTROL, VK_D);
            ready.Set();

            while (GetMessage(out var msg, _hwnd, 0, 0))
            {
                if (msg.message == WM_HOTKEY && msg.wParam.ToInt32() == _hotkeyId)
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        })
        { IsBackground = true, Name = "HotkeyThread" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return registered;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hwnd, _hotkeyId);
                DestroyWindow(_hwnd);
            }
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

public class TextExtractor
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern IntPtr ChildWindowFromPoint(IntPtr hwndParent, POINT point);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    private const uint WM_GETTEXT       = 0x000D;
    private const uint WM_GETTEXTLENGTH = 0x000E;

    [StructLayout(LayoutKind.Sequential)] private struct RECT  { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    public static string? GetTextUnderCursor()
    {
        try
        {
            GetCursorPos(out POINT cursorPos);
            var hwnd = WindowFromPoint(cursorPos);
            if (hwnd == IntPtr.Zero) return null;

            var childHwnd = ChildWindowFromPoint(hwnd, cursorPos);
            if (childHwnd != IntPtr.Zero) hwnd = childHwnd;

            int length = SendMessageTimeout(hwnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero,
                0x0002, 1000, out _).ToInt32();
            if (length == 0) return null;

            IntPtr buffer = Marshal.AllocHGlobal((length + 1) * 2);
            try
            {
                SendMessage(hwnd, WM_GETTEXT, (IntPtr)(length + 1), buffer);
                return Marshal.PtrToStringUni(buffer);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch { return null; }
    }
}
#else
namespace AlbionPrices.Helpers;

public class GlobalHotkey : IDisposable
{
    public event EventHandler? HotkeyPressed;
    public GlobalHotkey(int hotkeyId) { }
    public bool Register() => false;
    public void Dispose() { GC.SuppressFinalize(this); }
}

public static class TextExtractor
{
    public static string? GetTextUnderCursor() => null;
}
#endif
