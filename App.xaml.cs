namespace Uninstaller;

public partial class App : Application
{
#if WINDOWS
    private Platforms.Windows.TrayIcon? _tray;

    /// <summary>El icono de bandeja, para que Ajustes le cambie el comportamiento al vuelo.</summary>
    public static Platforms.Windows.TrayIcon? Tray { get; private set; }
#endif

    public App()
    {
        InitializeComponent();
    }

#if WINDOWS
    private static bool IsPackaged()
    {
        try { return global::Windows.ApplicationModel.Package.Current is not null; }
        catch (Exception) { return false; }
    }
#endif

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
        Pages.DiskUsagePage.PendingMap = args.Contains("--map");   // y directo al mapa de rectangulos
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
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
                var settings = Helpers.ServiceHelper.GetRequiredService<Services.ISettingsService>();
                _tray = new Platforms.Windows.TrayIcon(hwnd, key => loc[key], () => native.Close()) { MinimizeToTray = settings.TrayOnMinimize };
                Tray = _tray;
                // «--tray» (arranque con Windows): escondida en la bandeja desde el principio.
                if (Environment.GetCommandLineArgs().Contains("--tray"))
                    native.DispatcherQueue.TryEnqueue(() => _tray.HideToTray());
                // Sin paquete (exe suelto o lanzador): identidad para la barra de tareas y anclaje al lanzador.
                if (!IsPackaged())
                    Platforms.Windows.TaskbarIdentity.Apply(hwnd, "sOCratic.sOCUninstaller", "sOC Uninstaller", Environment.GetEnvironmentVariable("SOC_LAUNCHER"));
            }
        };
#endif
#if DEBUG
        SocShared.AuthorNotes.Attach(window);   // notas de autor: SOLO Debug, desactivado en Release/produccion
#endif
        return window;
    }
}
