using Android.Widget;
using Uninstaller.Services;
using AndroidApp = Android.App.Application;

namespace Uninstaller.Platforms.Android;

/// <inheritdoc cref="IToastService"/>
public class ToastService : IToastService
{
    public void Show(string message) =>
        // Los toasts de Android solo pueden crearse desde el hilo de interfaz.
        MainThread.BeginInvokeOnMainThread(() =>
            Toast.MakeText(AndroidApp.Context, message, ToastLength.Short)?.Show());
}
