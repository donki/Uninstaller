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
/// Solo el puente con el PackageManager y el intent de desinstalar (constitucion 5): que paquetes
/// entran, sus fechas y tamaños y la espera del dialogo estan en <see cref="AndroidPackages"/> y
/// <see cref="UninstallWaiter"/>, con pruebas. Android NO permite el borrado masivo silencioso:
/// cada desinstalacion la confirma el usuario en un dialogo del sistema, en secuencia.
/// </remarks>
public class AppInventoryService : IAppInventoryService
{
    // La resuelve MainActivity.OnActivityResult.
    private static readonly UninstallWaiter Waiter = new();

    public Task<IReadOnlyList<InstalledApp>> GetInstalledAppsAsync(bool includeSystem)
    {
        // El inventario y la conversion de iconos son costosos: fuera del hilo de interfaz.
        return Task.Run<IReadOnlyList<InstalledApp>>(() =>
        {
            var context = AndroidApp.Context;
            var pm = context.PackageManager ?? throw new InvalidOperationException("PackageManager is not available.");
            // GetInstalledPackages (no GetInstalledApplications) para tener FirstInstallTime/LastUpdateTime.
            var packages = pm.GetInstalledPackages(PackageInfoFlags.MetaData)
                .Where(pi => pi.ApplicationInfo is not null)
                .Select(pi => ToPackage(pm, pi, pi.ApplicationInfo!));
            return AndroidPackages.Build(packages, context.PackageName, includeSystem);
        });
    }

    private static AndroidPackage ToPackage(PackageManager pm, PackageInfo pi, ApplicationInfo ai) => new(
        pi.PackageName ?? ai.PackageName,
        (ai.Flags & ApplicationInfoFlags.System) != 0,
        (ai.Flags & ApplicationInfoFlags.UpdatedSystemApp) != 0,
        pi.FirstInstallTime,
        pi.LastUpdateTime,
        ai.PublicSourceDir ?? ai.SourceDir,
        ai.SplitPublicSourceDirs ?? ai.SplitSourceDirs,
        () => pm.GetApplicationLabel(ai)?.ToString(),
        () => ToImageSource(pm.GetApplicationIcon(ai)));

    public Task<bool> UninstallAsync(string packageName, bool unattended)
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
            return Task.FromResult(false);
        var task = Waiter.Start();
        var intent = new Intent(Intent.ActionUninstallPackage);
        intent.SetData(Uri.Parse("package:" + packageName));
        intent.PutExtra(Intent.ExtraReturnResult, true);
        activity.StartActivityForResult(intent, UninstallWaiter.RequestCode);
        return task;
    }

    /// <summary>Puente desde MainActivity.OnActivityResult: resuelve la desinstalacion en curso.</summary>
    public static void OnUninstallResult(int requestCode, Result resultCode) => Waiter.Complete(requestCode, resultCode == Result.Ok);

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
