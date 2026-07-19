namespace Uninstaller.Services;

/// <summary>
/// Aviso breve y no bloqueante tras una accion. La implementacion es especifica de Android
/// y vive en Platforms/Android (constitucion 5).
/// </summary>
public interface IToastService
{
    void Show(string message);
}
