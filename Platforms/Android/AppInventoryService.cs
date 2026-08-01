using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Uninstaller.Models;
using Uninstaller.Services;
using AndroidApp = Android.App.Application;
using Result = Android.App.Result;
using Uri = Android.Net.Uri;

namespace Uninstaller.Platforms.Android;

/// <inheritdoc cref="IAppInventoryService"/>
/// <remarks>
/// Toda la logica de plataforma (PackageManager, intents de desinstalacion) vive aqui
/// (constitucion 5). Android NO permite el borrado masivo silencioso: cada desinstalacion la
/// confirma el usuario en un dialogo del sistema, que se lanza por cada app en secuencia.
/// </remarks>
public class AppInventoryService : IAppInventoryService
{
    private const int UninstallRequestCode = 4711;

    // Espera activa de la desinstalacion en curso; la resuelve MainActivity.OnActivityResult.
    private static TaskCompletionSource<bool>? _pendingUninstall;

    public Task<IReadOnlyList<InstalledApp>> GetInstalledAppsAsync(bool includeSystem)
    {
        // El inventario y la conversion de iconos son costosos: fuera del hilo de interfaz.
        return Task.Run<IReadOnlyList<InstalledApp>>(() =>
        {
            var context = AndroidApp.Context;
            var pm = context.PackageManager
                     ?? throw new InvalidOperationException("PackageManager is not available.");
            var ownPackage = context.PackageName;

            var result = new List<InstalledApp>();

            // GetInstalledPackages (no GetInstalledApplications) para tener FirstInstallTime/LastUpdateTime.
            foreach (var pi in pm.GetInstalledPackages(PackageInfoFlags.MetaData))
            {
                var ai = pi.ApplicationInfo;
                if (ai is null)
                    continue;

                var package = pi.PackageName ?? ai.PackageName;
                if (string.IsNullOrEmpty(package) || package == ownPackage)
                    continue;

                // Una app del sistema actualizada por el usuario se trata como de usuario.
                var isSystem = (ai.Flags & ApplicationInfoFlags.System) != 0
                               && (ai.Flags & ApplicationInfoFlags.UpdatedSystemApp) == 0;
                if (!includeSystem && isSystem)
                    continue;

                string label;
                try { label = pm.GetApplicationLabel(ai)?.ToString() ?? package; }
                catch { label = package; }

                ImageSource? icon = null;
                try { icon = ToImageSource(pm.GetApplicationIcon(ai)); }
                catch { /* algunas apps no exponen icono: se muestra sin el */ }

                result.Add(new InstalledApp
                {
                    PackageName = package,
                    Label = label,
                    IsSystem = isSystem,
                    InstallDate = FromMillis(pi.FirstInstallTime),
                    UpdatedDate = FromMillis(pi.LastUpdateTime),
                    Icon = icon
                });
            }

            // Orden por defecto: fecha de instalacion (mas reciente primero). La UI puede reordenar.
            result.Sort((a, b) => b.InstallDate.CompareTo(a.InstallDate));
            return result;
        });
    }

    public Task<bool> UninstallAsync(string packageName)
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
            return Task.FromResult(false);

        // Si quedo una espera anterior sin resolver, se cierra para no bloquear.
        _pendingUninstall?.TrySetResult(false);

        var tcs = new TaskCompletionSource<bool>();
        _pendingUninstall = tcs;

        var intent = new Intent(Intent.ActionUninstallPackage);
        intent.SetData(Uri.Parse("package:" + packageName));
        intent.PutExtra(Intent.ExtraReturnResult, true);
        activity.StartActivityForResult(intent, UninstallRequestCode);

        return tcs.Task;
    }

    /// <summary>Puente desde MainActivity.OnActivityResult: resuelve la desinstalacion en curso.</summary>
    public static void OnUninstallResult(int requestCode, Result resultCode)
    {
        if (requestCode != UninstallRequestCode)
            return;

        var tcs = _pendingUninstall;
        _pendingUninstall = null;
        // Con EXTRA_RETURN_RESULT, RESULT_OK significa desinstalada.
        tcs?.TrySetResult(resultCode == Result.Ok);
    }

    private static DateTime FromMillis(long millis)
    {
        if (millis <= 0)
            return DateTime.MinValue;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(millis).LocalDateTime; }
        catch { return DateTime.MinValue; }
    }

    private static ImageSource? ToImageSource(Drawable? drawable)
    {
        if (drawable is null)
            return null;

        Bitmap? bitmap;
        if (drawable is BitmapDrawable bd && bd.Bitmap is not null)
        {
            bitmap = bd.Bitmap;
        }
        else
        {
            var w = Math.Clamp(drawable.IntrinsicWidth, 1, 144);
            var h = Math.Clamp(drawable.IntrinsicHeight, 1, 144);
            bitmap = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!);
            using var canvas = new Canvas(bitmap);
            drawable.SetBounds(0, 0, canvas.Width, canvas.Height);
            drawable.Draw(canvas);
        }

        if (bitmap is null)
            return null;

        using var stream = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
        var bytes = stream.ToArray();
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }
}
