using Uninstaller.Models;

namespace Uninstaller.Services;

/// <summary>
/// Inventario de aplicaciones instaladas y lanzamiento de la desinstalacion. La logica de
/// plataforma (PackageManager, intents) se encapsula en Platforms/Android (constitucion 5).
/// </summary>
public interface IAppInventoryService
{
    /// <summary>
    /// Devuelve las aplicaciones instaladas ordenadas por nombre. Si <paramref name="includeSystem"/>
    /// es falso, solo las del usuario. Excluye siempre la propia aplicacion.
    /// </summary>
    Task<IReadOnlyList<InstalledApp>> GetInstalledAppsAsync(bool includeSystem);

    /// <summary>
    /// Desinstala un paquete y espera a que termine. Devuelve <c>true</c> si quedo desinstalado.
    /// En Android lanza el intent del sistema, que el usuario confirma (no hay borrado masivo
    /// silencioso; <paramref name="unattended"/> se ignora). En Windows abre el desinstalador del
    /// programa; con <paramref name="unattended"/> lo lanza sin preguntas cuando el instalador lo
    /// admite (Windows Installer, Inno Setup, NSIS, QuietUninstallString y apps de la Store).
    /// </summary>
    Task<bool> UninstallAsync(string packageName, bool unattended);
}
