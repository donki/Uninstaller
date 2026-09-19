using System.Runtime.InteropServices;

namespace Uninstaller.Platforms.Windows;

/// <summary>
/// Al minimizar, la ventana se esconde y queda un icono en el area de notificacion; clic para
/// volver, boton derecho para «Abrir» o «Salir». WinUI no trae icono de bandeja, asi que va con
/// Shell_NotifyIcon y una subclase del procedimiento de la ventana (para cazar SC_MINIMIZE).
/// </summary>
public sealed class TrayIcon
{
    private const int WmSysCommand = 0x0112;
    private const int WmCommand = 0x0111;
    private const int WmRButtonUp = 0x0205;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmTray = 0x8001;   // WM_APP + 1
    private const int ScMinimize = 0xF020;
    private const int IdOpen = 1, IdExit = 2;

    private readonly IntPtr _hwnd;
    private readonly WndProc _proc;
    private readonly IntPtr _oldProc;
    private readonly Func<string, string> _text;
    private readonly Action _exit;
    private bool _shown;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Si al minimizar se esconde en la bandeja (ajuste del usuario) o se minimiza como siempre.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Esconder ahora (arranque con --tray).</summary>
    public void HideToTray() => Hide();

    public TrayIcon(IntPtr hwnd, Func<string, string> text, Action exit)
    {
        _hwnd = hwnd;
        _text = text;
        _exit = exit;
        _proc = HandleMessage;
        _oldProc = SetWindowLongPtr(hwnd, -4 /* GWLP_WNDPROC */, Marshal.GetFunctionPointerForDelegate(_proc));
    }

    private IntPtr HandleMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmSysCommand when ((long)wParam & 0xFFF0) == ScMinimize && MinimizeToTray:
                Hide();
                return IntPtr.Zero;
            case WmTray:
                var evt = (int)((long)lParam & 0xFFFF);
                if (evt is WmLButtonUp or WmLButtonDblClk)
                    Restore();
                else if (evt == WmRButtonUp)
                    ShowMenu();
                return IntPtr.Zero;
            case WmCommand when (int)((long)wParam & 0xFFFF) == IdOpen:
                Restore();
                return IntPtr.Zero;
            case WmCommand when (int)((long)wParam & 0xFFFF) == IdExit:
                Remove();
                _exit();
                return IntPtr.Zero;
        }
        return CallWindowProc(_oldProc, hWnd, msg, wParam, lParam);
    }

    private void Hide()
    {
        Add();
        ShowWindow(_hwnd, 0 /* SW_HIDE */);
    }

    private void Restore()
    {
        ShowWindow(_hwnd, 9 /* SW_RESTORE */);
        SetForegroundWindow(_hwnd);
        Remove();
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, 0, IdOpen, _text("TrayOpen"));
        AppendMenu(menu, 0x800 /* MF_SEPARATOR */, 0, null);
        AppendMenu(menu, 0, IdExit, _text("TrayExit"));
        GetCursorPos(out var p);
        // Sin esto el menu no se cierra al pinchar fuera (documentado en TrackPopupMenu).
        SetForegroundWindow(_hwnd);
        TrackPopupMenu(menu, 0x0080 /* TPM_RIGHTBUTTON */, p.X, p.Y, 0, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
    }

    private void Add()
    {
        if (_shown)
            return;
        var data = Data();
        data.uFlags = 0x1 | 0x2 | 0x4;   // NIF_MESSAGE | NIF_ICON | NIF_TIP
        data.uCallbackMessage = WmTray;
        data.hIcon = LoadAppIcon();
        data.szTip = "sOC Uninstaller";
        Shell_NotifyIcon(0 /* NIM_ADD */, ref data);
        _shown = true;
    }

    private void Remove()
    {
        if (!_shown)
            return;
        var data = Data();
        Shell_NotifyIcon(2 /* NIM_DELETE */, ref data);
        _shown = false;
    }

    private NotifyIconData Data() => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _hwnd,
        uID = 1,
    };

    /// <summary>El icono del propio ejecutable; si no lo tiene, el generico de aplicacion.</summary>
    private static IntPtr LoadAppIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (path is not null)
            {
                var large = new IntPtr[1];
                var small = new IntPtr[1];
                if (ExtractIconEx(path, 0, large, small, 1) > 0 && small[0] != IntPtr.Zero)
                    return small[0];
            }
        }
        catch (Exception) { }
        return LoadIcon(IntPtr.Zero, new IntPtr(32512) /* IDI_APPLICATION */);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, int flags, int id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool TrackPopupMenu(IntPtr menu, int flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int count);
}
