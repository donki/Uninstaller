namespace Uninstaller.Helpers;

/// <summary>
/// Acceso al contenedor de dependencias desde paginas y Shell, que MAUI no inyecta
/// automaticamente al navegar (constitucion 5: inyeccion de dependencias para todos los servicios).
/// </summary>
public static class ServiceHelper
{
    private static IServiceProvider? _services;

    public static void Initialize(IServiceProvider services) => _services = services;

    public static T GetRequiredService<T>() where T : notnull
    {
        if (_services is null)
            throw new InvalidOperationException("ServiceHelper was used before MauiProgram initialized it.");

        return _services.GetRequiredService<T>();
    }
}
