using Uninstaller.Services;

namespace Uninstaller.Platforms.Windows;

/// <inheritdoc cref="IToastService"/>
/// <remarks>
/// Windows no tiene «toast» de aplicacion sin registrar notificaciones: se enseña como una
/// franja breve dentro de la propia pagina (<see cref="ToastRequested"/>), que la pagina principal
/// pinta y esconde sola a los pocos segundos.
/// </remarks>
public class ToastService : IToastService
{
    public static event Action<string>? ToastRequested;

    public void Show(string message) =>
        MainThread.BeginInvokeOnMainThread(() => ToastRequested?.Invoke(message));
}
