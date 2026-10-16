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
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

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

    /// <summary>El menu con las medidas y colores de la regla General 6.14, en el tema de la app.</summary>
    private XamlControls.MenuFlyout BuildMenu(bool dark)
    {
        var card = Brush(dark ? "CardBackgroundDark" : "CardBackgroundLight");
        var separator = Brush(dark ? "SeparatorDark" : "SeparatorLight");
        var text = Brush(dark ? "TextPrimaryDark" : "TextPrimaryLight");
        var theme = dark ? Xaml.ElementTheme.Dark : Xaml.ElementTheme.Light;

        // Contenedor: tarjeta con borde de 1 px, esquinas de 10, relleno de 4, 240 de ancho y sin sombra.
        var presenterStyle = new Xaml.Style(typeof(XamlControls.MenuFlyoutPresenter));
        presenterStyle.Setters.Add(new Xaml.Setter(XamlControls.Control.BackgroundProperty, card));
        presenterStyle.Setters.Add(new Xaml.Setter(XamlControls.Control.BorderBrushProperty, separator));
        presenterStyle.Setters.Add(new Xaml.Setter(XamlControls.Control.BorderThicknessProperty, new Xaml.Thickness(1)));
        presenterStyle.Setters.Add(new Xaml.Setter(XamlControls.Control.CornerRadiusProperty, new Xaml.CornerRadius(10)));
        presenterStyle.Setters.Add(new Xaml.Setter(XamlControls.Control.PaddingProperty, new Xaml.Thickness(4)));
        presenterStyle.Setters.Add(new Xaml.Setter(Xaml.FrameworkElement.MinWidthProperty, 240d));
        presenterStyle.Setters.Add(new Xaml.Setter(Xaml.FrameworkElement.RequestedThemeProperty, theme));
        presenterStyle.Setters.Add(new Xaml.Setter(XamlControls.MenuFlyoutPresenter.IsDefaultShadowEnabledProperty, false));

        var menu = new XamlControls.MenuFlyout
        {
            MenuFlyoutPresenterStyle = presenterStyle,
            ShouldConstrainToRootBounds = false,
        };
        menu.Items.Add(Item(_text("TrayOpen"), "", Restore, dark, card, text));
        menu.Items.Add(Separator(separator));
        menu.Items.Add(Item(_text("TrayExit"), "", Exit, dark, card, text));
        return menu;
    }

    /// <summary>
    /// Elemento del menu: 32 px de alto, esquinas de 6, icono plano de 14 px en indigo (en rojo
    /// <c>Danger</c> si la accion es destructiva) y, bajo el raton, el fondo de seleccion.
    /// </summary>
    private static XamlControls.MenuFlyoutItem Item(string label, string glyph, Action action, bool dark,
        XamlMedia.Brush card, XamlMedia.Brush text, bool enabled = true, bool danger = false)
    {
        var item = new XamlControls.MenuFlyoutItem
        {
            Text = label,
            IsEnabled = enabled,
            MinHeight = 32,
            Padding = new Xaml.Thickness(8, 7, 10, 7),
            CornerRadius = new Xaml.CornerRadius(6),
            FontSize = 13,
            Foreground = text,
            Opacity = enabled ? 1 : 0.45,
            Icon = new XamlControls.FontIcon
            {
                Glyph = glyph,
                FontFamily = new XamlMedia.FontFamily(IconFont),
                FontSize = 14,
                Foreground = Brush(danger ? "Danger" : dark ? "PrimaryTextDark" : "PrimaryTextLight"),
            },
        };
        // Colores de los estados del propio control (estilo ligero de WinUI): los del tema de la app.
        var selection = Brush(dark ? "SelectionSurfaceDark" : "SelectionSurfaceLight");
        item.Resources["MenuFlyoutItemBackground"] = new XamlMedia.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        item.Resources["MenuFlyoutItemBackgroundPointerOver"] = selection;
        item.Resources["MenuFlyoutItemBackgroundPressed"] = selection;
        item.Resources["MenuFlyoutItemBackgroundDisabled"] = card;
        item.Resources["MenuFlyoutItemForeground"] = text;
        item.Resources["MenuFlyoutItemForegroundPointerOver"] = text;
        item.Resources["MenuFlyoutItemForegroundPressed"] = text;
        item.Resources["MenuFlyoutItemForegroundDisabled"] = text;
        // La accion, cuando el menu ya se ha cerrado (Salir cierra tambien la ventana auxiliar).
        item.Click += (_, _) => Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() => action());
        return item;
    }

    /// <summary>Separador: 1 px del color de separador con margenes de 8·4.</summary>
    private static XamlControls.MenuFlyoutSeparator Separator(XamlMedia.Brush color)
    {
        var separator = new XamlControls.MenuFlyoutSeparator { Padding = new Xaml.Thickness(8, 4, 8, 4) };
        separator.Resources["MenuFlyoutSeparatorBackground"] = color;
        separator.Resources["MenuFlyoutSeparatorThemeHeight"] = 1d;
        return separator;
    }

    private static bool IsDark() => Application.Current?.RequestedTheme == AppTheme.Dark;

    /// <summary>Un color de Resources/Styles/Colors.xaml como pincel de WinUI.</summary>
    private static XamlMedia.SolidColorBrush Brush(string key)
    {
        var c = Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color ? color : Colors.Gray;
        return new XamlMedia.SolidColorBrush(global::Windows.UI.Color.FromArgb(
            (byte)(c.Alpha * 255), (byte)(c.Red * 255), (byte)(c.Green * 255), (byte)(c.Blue * 255)));
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
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int count);
}
