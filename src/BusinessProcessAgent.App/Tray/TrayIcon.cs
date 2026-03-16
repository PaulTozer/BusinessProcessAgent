using System.Runtime.InteropServices;
using System.Drawing;

namespace BusinessProcessAgent.App.Tray;

/// <summary>
/// Thin wrapper around the Win32 Shell_NotifyIcon API to show a
/// system-tray icon from a WinUI 3 (unpackaged) application.
/// WinUI 3 has no built-in NotifyIcon, so we P/Invoke directly.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int WM_APP_TRAYICON = 0x8000 + 1;
    private const int NIM_ADD = 0x00;
    private const int NIM_DELETE = 0x02;
    private const int NIM_MODIFY = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const int NIF_MESSAGE = 0x01;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    private NOTIFYICONDATA _nid;
    private readonly TrayMessageWindow _messageWindow;
    private bool _disposed;

    /// <summary>Fired when the user left-clicks the tray icon.</summary>
    public event Action? LeftClick;

    /// <summary>Fired when the user right-clicks the tray icon.</summary>
    public event Action? RightClick;

    public TrayIcon(string tooltip)
    {
        _messageWindow = new TrayMessageWindow(OnTrayMessage);

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _messageWindow.Handle,
            uID = 1,
            uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE,
            uCallbackMessage = WM_APP_TRAYICON,
            szTip = tooltip,
            hIcon = LoadDefaultIcon(),
        };

        Shell_NotifyIcon(NIM_ADD, ref _nid);
    }

    public void UpdateTooltip(string tooltip)
    {
        _nid.szTip = tooltip;
        _nid.uFlags = NIF_TIP;
        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
    }

    private void OnTrayMessage(int msg)
    {
        switch (msg)
        {
            case WM_LBUTTONUP: LeftClick?.Invoke(); break;
            case WM_RBUTTONUP: RightClick?.Invoke(); break;
        }
    }

    private static IntPtr LoadDefaultIcon()
    {
        // Use the default application icon
        return LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shell_NotifyIcon(NIM_DELETE, ref _nid);
        _messageWindow.Dispose();
    }

    // ── P/Invoke ──

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }
}

/// <summary>
/// Hidden message-only window that receives tray icon callback messages.
/// </summary>
internal sealed class TrayMessageWindow : IDisposable
{
    private const string ClassName = "BPA_TrayMsgWnd";
    private const int WM_APP_TRAYICON = 0x8000 + 1;

    private readonly IntPtr _hWnd;
    private readonly WndProcDelegate _wndProc;
    private readonly Action<int> _callback;
    private bool _disposed;

    public IntPtr Handle => _hWnd;

    internal delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public TrayMessageWindow(Action<int> callback)
    {
        _callback = callback;
        _wndProc = WndProc;

        var wc = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            lpszClassName = ClassName,
        };
        RegisterClass(ref wc);

        _hWnd = CreateWindowEx(0, ClassName, "", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_APP_TRAYICON)
        {
            _callback((int)lParam);
            return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DestroyWindow(_hWnd);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName,
        int style, int x, int y, int width, int height,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
    }
}
