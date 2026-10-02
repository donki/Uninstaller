namespace Uninstaller.Services;

/// <summary>Lo que Ajustes toca de Windows: arrancar con el sistema y el icono de la bandeja.</summary>
public interface IDesktopIntegration
{
    bool StartsWithWindows { get; }

    void SetStartWithWindows(bool enabled);

    /// <summary>Aplica al vuelo si minimizar esconde la ventana en la bandeja.</summary>
    void SetMinimizeToTray(bool enabled);
}

/// <inheritdoc cref="IDesktopIntegration"/>
public class DesktopIntegration : IDesktopIntegration
{
    private const string RunValue = "sOCUninstaller";

#if WINDOWS
    public bool StartsWithWindows => Platforms.Windows.WindowsStartup.IsEnabled(RunValue);

    public void SetStartWithWindows(bool enabled) => Platforms.Windows.WindowsStartup.Set(RunValue, enabled);

    public void SetMinimizeToTray(bool enabled)
    {
        if (App.Tray is { } tray)
            tray.MinimizeToTray = enabled;
    }
#else
    public bool StartsWithWindows => false;

    public void SetStartWithWindows(bool enabled) { }

    public void SetMinimizeToTray(bool enabled) { }
#endif
}
