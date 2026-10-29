using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls.Primitives;
using Xaml = Microsoft.UI.Xaml;
using XamlControls = Microsoft.UI.Xaml.Controls;
using XamlMedia = Microsoft.UI.Xaml.Media;

namespace Uninstaller.Platforms.Windows;

/// <summary>
/// Al minimizar, la ventana se esconde y queda un icono en el area de notificacion; clic para
/// volver, boton derecho para «Abrir» o «Salir». WinUI no trae icono de bandeja, asi que va con
/// Shell_NotifyIcon y una subclase del procedimiento de la ventana (para cazar SC_MINIMIZE).
/// El menu no es el gris de Win32 sino un MenuFlyout de WinUI con el aspecto comun de las apps
/// sOC (General 6.14, el de sOC ScreenShare): se abre desde una ventana auxiliar de 1 px sin
/// bordes que se pone bajo el puntero, pasa a primer plano y se cierra con el menu.
/// </summary>
public sealed class TrayIcon
{
    private const int WmSysCommand = 0x0112;
    private const int WmRButtonUp = 0x0205;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmTray = 0x8001;   // WM_APP + 1
    private const int ScMinimize = 0xF020;

    private readonly IntPtr _hwnd;
    private readonly WndProc _proc;
    private readonly IntPtr _oldProc;
    private readonly Func<string, string> _text;
    private readonly Action _exit;
    private bool _shown;
    private Xaml.Window? _menuHost;   // ventana auxiliar del menu mientras esta abierto

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

    private void Exit()
    {
        CloseMenuHost();
        Remove();
        _exit();
    }

    private void ShowMenu()
    {
        CloseMenuHost();
        GetCursorPos(out var p);

        // Ventana auxiliar de 1 px bajo el puntero: sin bordes, siempre encima y fuera de la barra
        // de tareas y de Alt+Tab. El menu sale de ella y, como no se limita a su tamaño
        // (ShouldConstrainToRootBounds), se ve entero junto al puntero.
        var dark = IsDark();
        var anchor = new XamlControls.Grid
        {
            Background = new XamlMedia.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            RequestedTheme = dark ? Xaml.ElementTheme.Dark : Xaml.ElementTheme.Light,
        };
        var host = new Xaml.Window { Content = anchor, Title = "sOC Uninstaller" };
        _menuHost = host;
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        host.AppWindow.SetPresenter(presenter);
        host.AppWindow.IsShownInSwitchers = false;
        host.AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(p.X, p.Y, 1, 1));

        var menu = BuildMenu(dark);
        menu.Closed += (_, _) => host.DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(_menuHost, host))
                CloseMenuHost();
        });
        // Al pinchar fuera la ventana auxiliar pierde el primer plano: se cierra el menu, salvo
        // que el primer plano siga siendo nuestro (el propio menu).
        host.Activated += (_, e) =>
        {
            if (e.WindowActivationState != Xaml.WindowActivationState.Deactivated)
                return;
            host.DispatcherQueue.TryEnqueue(() =>
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
                if (pid != (uint)Environment.ProcessId && menu.IsOpen)
                    menu.Hide();
            });
        };

        // El menu necesita la ventana ya cargada (XamlRoot) para abrirse.
        anchor.Loaded += (_, _) =>
        {
            if (ReferenceEquals(_menuHost, host))
                menu.ShowAt(anchor, new FlyoutShowOptions { Position = new global::Windows.Foundation.Point(0, 0) });
        };
        host.Activate();
        // Sin esto el menu no se cierra al pinchar fuera: tiene que estar en primer plano.
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(host));
    }

    private void CloseMenuHost()
    {
        var host = _menuHost;
        _menuHost = null;
        try { host?.Close(); }
        catch (Exception) { }
    }

    /// <summary>El menu con las medidas y colores de la regla General 6.14 (<see cref="SocMenu"/>), en el tema de la app.</summary>
    private XamlControls.MenuFlyout BuildMenu(bool dark)
    {
        var menu = new SocMenu(dark);
        menu.Flyout.Items.Add(menu.Item(_text("TrayOpen"), "", Restore));
        menu.Flyout.Items.Add(menu.Separator());
        menu.Flyout.Items.Add(menu.Item(_text("TrayExit"), "", Exit));
        return menu.Flyout;
    }

    private static bool IsDark() => SocMenu.IsDark;

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
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int count);
}
