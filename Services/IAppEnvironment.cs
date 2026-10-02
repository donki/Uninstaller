namespace Uninstaller.Services;

/// <summary>
/// Lo que la logica necesita del sistema y no se puede ejecutar fuera del dispositivo: la version
/// instalada, abrir un enlace en el navegador y redactar un correo.
/// </summary>
public interface IAppEnvironment
{
    string VersionString { get; }

    Task OpenUrlAsync(Uri uri);

    /// <summary>Abre el cliente de correo. False si el dispositivo no tiene ninguno.</summary>
    Task<bool> ComposeEmailAsync(string subject, string to);
}
