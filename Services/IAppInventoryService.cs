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
    /// Lanza el intent del sistema para desinstalar un paquete y espera a que el usuario
    /// confirme o cancele. Devuelve <c>true</c> si el paquete quedo desinstalado.
    /// Android no permite el borrado masivo silencioso: cada app la confirma el usuario.
    /// </summary>
    Task<bool> UninstallAsync(string packageName);
}
