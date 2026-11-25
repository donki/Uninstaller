using Uninstaller.Models;

namespace Uninstaller.Services;

/// <summary>Lo que el PackageManager de Android cuenta de un paquete, ya sin tipos de Android.</summary>
/// <param name="PackageName">PackageInfo.PackageName (o, si falta, el de su ApplicationInfo).</param>
/// <param name="SystemFlag">ApplicationInfoFlags.System.</param>
/// <param name="UpdatedSystemFlag">ApplicationInfoFlags.UpdatedSystemApp.</param>
/// <param name="FirstInstallMillis">PackageInfo.FirstInstallTime (milisegundos Unix).</param>
/// <param name="LastUpdateMillis">PackageInfo.LastUpdateTime (milisegundos Unix).</param>
/// <param name="BaseApk">ApplicationInfo.PublicSourceDir (o SourceDir).</param>
/// <param name="Splits">ApplicationInfo.SplitPublicSourceDirs (o SplitSourceDirs): los trozos de un App Bundle.</param>
/// <param name="Label">El nombre visible; puede fallar (se usa entonces el paquete).</param>
/// <param name="Icon">El icono; puede fallar o no haberlo.</param>
public sealed record AndroidPackage(
    string? PackageName, bool SystemFlag, bool UpdatedSystemFlag, long FirstInstallMillis, long LastUpdateMillis,
    string? BaseApk, IEnumerable<string?>? Splits, Func<string?> Label, Func<ImageSource?> Icon);

/// <summary>
/// La parte de Android que no necesita Android: que paquetes entran en la lista, sus fechas y su
/// tamaño, y la espera del dialogo de desinstalar del sistema. La plataforma solo traduce el
/// PackageManager a <see cref="AndroidPackage"/> y lanza el intent.
/// </summary>
public static class AndroidPackages
{
    /// <summary>La lista que ve el usuario: sin la propia app, sin las del sistema si no se piden, de la mas reciente a la mas antigua.</summary>
    public static List<InstalledApp> Build(IEnumerable<AndroidPackage> packages, string? ownPackage, bool includeSystem)
    {
        var result = new List<InstalledApp>();
        foreach (var p in packages)
        {
            if (string.IsNullOrEmpty(p.PackageName) || p.PackageName == ownPackage)
                continue;
            // Una app del sistema actualizada por el usuario se trata como de usuario.
            var isSystem = p.SystemFlag && !p.UpdatedSystemFlag;
            if (!includeSystem && isSystem)
                continue;
            string label;
            try { label = p.Label() ?? p.PackageName; }
            catch (Exception) { label = p.PackageName; }
            ImageSource? icon = null;
            try { icon = p.Icon(); }
            catch (Exception) { /* algunas apps no exponen icono: se muestra sin el */ }
            result.Add(new InstalledApp
            {
                PackageName = p.PackageName,
                Label = label,
                IsSystem = isSystem,
                InstallDate = FromMillis(p.FirstInstallMillis),
                UpdatedDate = FromMillis(p.LastUpdateMillis),
                SizeBytes = ApkSize(p.BaseApk, p.Splits),
                Icon = icon,
            });
        }
        // Orden por defecto: fecha de instalacion (mas reciente primero). La UI puede reordenar.
        result.Sort((a, b) => b.InstallDate.CompareTo(a.InstallDate));
        return result;
    }

    /// <summary>Milisegundos Unix a hora local; 0, negativos o fuera de rango, la fecha minima.</summary>
    public static DateTime FromMillis(long millis)
    {
        if (millis <= 0)
            return DateTime.MinValue;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(millis).LocalDateTime; }
        catch (ArgumentOutOfRangeException) { return DateTime.MinValue; }
    }

    /// <summary>
    /// Tamaño de los APK instalados: el base mas los trozos que genera un App Bundle. Es lo que se
    /// puede leer sin permisos; el total con datos y cache exigiria PACKAGE_USAGE_STATS, un permiso
    /// que el usuario tendria que dar a mano (constitucion 3: solo los imprescindibles).
    /// </summary>
    public static long ApkSize(string? baseApk, IEnumerable<string?>? splits)
    {
        long total = 0;
        foreach (var file in new[] { baseApk }.Concat(splits ?? []))
        {
            try
            {
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                    total += new FileInfo(file).Length;
            }
            catch (Exception)
            {
                // Alguna app del sistema no deja leer su APK: cuenta sin el.
            }
        }
        return total;
    }
}

/// <summary>
/// La espera del dialogo de desinstalar de Android: se abre con un intent y el resultado llega
/// despues a la actividad (OnActivityResult). Solo hay una en curso; si se abre otra, la anterior
/// se da por no desinstalada para no dejar a nadie esperando.
/// </summary>
public sealed class UninstallWaiter
{
    public const int RequestCode = 4711;

    private TaskCompletionSource<bool>? _pending;

    public Task<bool> Start()
    {
        _pending?.TrySetResult(false);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = tcs;
        return tcs.Task;
    }

    /// <summary>Resultado de la actividad: con EXTRA_RETURN_RESULT, OK quiere decir desinstalada.</summary>
    public void Complete(int requestCode, bool ok)
    {
        if (requestCode != RequestCode)
            return;
        var tcs = _pending;
        _pending = null;
        tcs?.TrySetResult(ok);
    }
}
