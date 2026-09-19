using Microsoft.Win32;

namespace Uninstaller.Platforms.Windows;

/// <summary>
/// «Arrancar con Windows»: una entrada en HKCU\…\Run con el lanzador (o el exe, si se corre suelto)
/// y el argumento --tray, para que arranque escondido en la bandeja. Solo el usuario actual y sin
/// tocar nada del sistema; se quita borrando el valor.
/// </summary>
internal static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>El exe que hay que arrancar: el lanzador que abrio la aplicacion, o el propio exe.</summary>
    public static string ExecutablePath =>
        Environment.GetEnvironmentVariable("SOC_LAUNCHER") is { Length: > 0 } launcher && File.Exists(launcher)
            ? launcher
            : Environment.ProcessPath ?? string.Empty;

    public static bool IsEnabled(string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(valueName) is string s && s.Length > 0;
        }
        catch (Exception) { return false; }
    }

    public static void Set(string valueName, bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
                return;
            if (enabled && ExecutablePath.Length > 0)
                key.SetValue(valueName, "\"" + ExecutablePath + "\" --tray");
            else
                key.DeleteValue(valueName, throwOnMissingValue: false);
        }
        catch (Exception)
        {
            // Sin permiso sobre HKCU no hay mas que hacer; el interruptor se queda como estaba.
        }
    }
}
