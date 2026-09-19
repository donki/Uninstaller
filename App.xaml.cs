namespace Uninstaller;

public partial class App : Application
{
#if WINDOWS
    private Platforms.Windows.TrayIcon? _tray;
#endif

    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
#if WINDOWS
        // «--disk [ruta]»: abrir directamente el espacio en disco y, con ruta, escanearla ya
        // (para accesos directos y para probarlo sin tocar la interfaz). Antes de crear el Shell,
        // que es quien mira la marca al cargarse.
        var args = Environment.GetCommandLineArgs();
        var disk = Array.IndexOf(args, "--disk");
        if (disk >= 0)
            Pages.DiskUsagePage.PendingPath = disk + 1 < args.Length && !args[disk + 1].StartsWith("--") ? args[disk + 1] : string.Empty;
#endif
        var window = new Window(new AppShell()) { Title = "sOC Uninstaller" };
#if WINDOWS
        // Tamaño de arranque razonable en el escritorio: la lista es alta y estrecha, como en el movil.
        window.Width = 720;
        window.Height = 820;
        // Al minimizar, a la bandeja (icono en el area de notificacion con Abrir y Salir).
        window.HandlerChanged += (_, _) =>
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native && _tray is null)
            {
                var loc = Helpers.ServiceHelper.GetRequiredService<Services.ILocalizationService>();
                _tray = new Platforms.Windows.TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(native), key => loc[key], () => native.Close());
            }
        };
#endif
#if DEBUG
        SocShared.AuthorNotes.Attach(window);   // notas de autor: SOLO Debug, desactivado en Release/produccion
#endif
        return window;
    }
}
