using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;
using Uninstaller.Models;
using Uninstaller.Services;
using Windows.Management.Deployment;

namespace Uninstaller.Platforms.Windows;

/// <inheritdoc cref="IAppInventoryService"/>
/// <remarks>
/// <para>Lo mismo que hace la version Android con el PackageManager, pero con lo que Windows tiene:</para>
/// <list type="bullet">
/// <item><b>Programas Win32</b> (instaladores clasicos y MSI): las claves <c>Uninstall</c> del
/// registro, de 64 bits, de 32 bits (WOW6432Node) y del usuario. De ahi salen nombre, editor,
/// version, fecha de instalacion, tamaño estimado, icono y la orden de desinstalar.</item>
/// <item><b>Paquetes MSIX</b> (Microsoft Store y apps de la caja): el <c>PackageManager</c> del
/// sistema, que tambien los quita sin dialogo.</item>
/// </list>
/// <para>«Apps del sistema» son aqui los componentes de Windows (<c>SystemComponent=1</c> en el
/// registro, o paquetes firmados por el sistema). Igual que en Android, cada desinstalacion va en
/// secuencia: los programas Win32 abren el desinstalador del fabricante (con su propia
/// confirmacion y, si hace falta, el aviso de control de cuentas) y se espera a que termine; los
/// paquetes MSIX se quitan directamente.</para>
/// </remarks>
public class AppInventoryService : IAppInventoryService
{
    private const string Win32Prefix = "win32:";
    private const string MsixPrefix = "msix:";

    // Como desinstalar cada identificador de la ultima lista (la orden del registro o el nombre
    // completo del paquete): la pagina solo conoce el PackageName.
    private readonly Dictionary<string, Win32Entry> _win32 = new(StringComparer.OrdinalIgnoreCase);

    private sealed record Win32Entry(RegistryKey Hive, string KeyPath, string UninstallString, bool IsMsi);

    public Task<IReadOnlyList<InstalledApp>> GetInstalledAppsAsync(bool includeSystem)
    {
        return Task.Run<IReadOnlyList<InstalledApp>>(() =>
        {
            var result = new List<InstalledApp>();
            _win32.Clear();
            ReadRegistry(includeSystem, result);
            ReadPackages(includeSystem, result);
            result.Sort((a, b) => b.InstallDate.CompareTo(a.InstallDate));
            return result;
        });
    }

    public async Task<bool> UninstallAsync(string packageName)
    {
        if (packageName.StartsWith(MsixPrefix, StringComparison.Ordinal))
            return await RemovePackageAsync(packageName[MsixPrefix.Length..]);

        if (_win32.TryGetValue(packageName, out var entry))
            return await RunUninstallerAsync(entry);

        throw new InvalidOperationException("Unknown program: " + packageName);
    }

    // ------------------------------------------------------------------ Win32 (registro)

    private static readonly (RegistryKey Hive, string Path)[] UninstallKeys =
    [
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
    ];

    private void ReadRegistry(bool includeSystem, List<InstalledApp> result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, path) in UninstallKeys)
        {
            using var root = hive.OpenSubKey(path);
            if (root is null)
                continue;
            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(name);
                    if (key is null)
                        continue;
                    var display = key.GetValue("DisplayName") as string;
                    var uninstall = key.GetValue("QuietUninstallString") as string ?? key.GetValue("UninstallString") as string;
                    if (string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(uninstall))
                        continue;
                    // Las actualizaciones (KB…) y los parches de MSI no son programas.
                    if ((key.GetValue("ParentKeyName") as string)?.Length > 0 || (key.GetValue("ReleaseType") as string) is "Update" or "Hotfix" or "Security Update")
                        continue;
                    var isSystem = Convert.ToInt32(key.GetValue("SystemComponent", 0), CultureInfo.InvariantCulture) == 1;
                    if (isSystem && !includeSystem)
                        continue;
                    // El mismo programa puede estar en la clave de 64 y en la de 32 bits.
                    var version = key.GetValue("DisplayVersion") as string ?? string.Empty;
                    if (!seen.Add(display + "|" + version))
                        continue;

                    var id = Win32Prefix + name;
                    var isMsi = Convert.ToInt32(key.GetValue("WindowsInstaller", 0), CultureInfo.InvariantCulture) == 1
                                || uninstall.Contains("msiexec", StringComparison.OrdinalIgnoreCase);
                    _win32[id] = new Win32Entry(hive, path + "\\" + name, uninstall, isMsi);

                    var publisher = key.GetValue("Publisher") as string ?? string.Empty;
                    var installed = ParseInstallDate(key.GetValue("InstallDate") as string, key);
                    var sizeKb = Convert.ToInt64(key.GetValue("EstimatedSize", 0L), CultureInfo.InvariantCulture);
                    result.Add(new InstalledApp
                    {
                        PackageName = id,
                        Label = version.Length > 0 ? $"{display} {version}" : display,
                        IsSystem = isSystem,
                        InstallDate = installed,
                        UpdatedDate = installed,
                        SizeBytes = sizeKb * 1024,
                        Icon = ExtractIcon(key.GetValue("DisplayIcon") as string, uninstall),
                        Publisher = publisher,
                    });
                }
                catch (Exception)
                {
                    // Una clave rota no debe tirar el inventario entero.
                }
            }
        }
    }

    /// <summary>InstallDate viene como AAAAMMDD; si falta, vale la fecha de la propia clave del registro.</summary>
    private static DateTime ParseInstallDate(string? raw, RegistryKey key)
    {
        if (raw is { Length: 8 } && DateTime.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        try
        {
            var location = key.GetValue("InstallLocation") as string;
            if (!string.IsNullOrEmpty(location) && Directory.Exists(location))
                return Directory.GetCreationTime(location);
        }
        catch (Exception) { }
        return DateTime.MinValue;
    }

    /// <summary>El icono de DisplayIcon («ruta,indice») o, si no hay, el del ejecutable de desinstalar.</summary>
    private static ImageSource? ExtractIcon(string? displayIcon, string uninstall)
    {
        foreach (var candidate in new[] { displayIcon, uninstall })
        {
            var file = IconFile(candidate);
            if (file is null || !File.Exists(file))
                continue;
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(file);
                if (icon is null)
                    continue;
                using var bitmap = icon.ToBitmap();
                var stream = new MemoryStream();
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                var bytes = stream.ToArray();
                return ImageSource.FromStream(() => new MemoryStream(bytes));
            }
            catch (Exception)
            {
                // Sin icono se enseña la fila igual.
            }
        }
        return null;
    }

    /// <summary>Quita comillas, el «,indice» de DisplayIcon y los argumentos de la orden de desinstalar.</summary>
    private static string? IconFile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = Environment.ExpandEnvironmentVariables(value.Trim());
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }
        var comma = text.LastIndexOf(',');
        if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), out _))
            text = text[..comma];
        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0)
            text = text[..(exe + 4)];
        else if (text.IndexOf(".ico", StringComparison.OrdinalIgnoreCase) is var ico && ico > 0)
            text = text[..(ico + 4)];
        else if (text.IndexOf(".dll", StringComparison.OrdinalIgnoreCase) is var dll && dll > 0)
            text = text[..(dll + 4)];
        return text.Contains("msiexec", StringComparison.OrdinalIgnoreCase) ? null : text;
    }

    /// <summary>
    /// Abre el desinstalador del fabricante (o msiexec) y espera a que acabe. Se da por
    /// desinstalado cuando su clave del registro ha desaparecido; algunos desinstaladores lanzan
    /// otro proceso y se cierran enseguida, asi que despues se le da un margen.
    /// </summary>
    private static async Task<bool> RunUninstallerAsync(Win32Entry entry)
    {
        var (file, arguments) = SplitCommand(entry.UninstallString);
        // «msiexec /I{…}» en UninstallString es «modificar»: para quitar hay que pedir /X.
        if (entry.IsMsi && arguments.Contains("/I", StringComparison.OrdinalIgnoreCase) && !arguments.Contains("/X", StringComparison.OrdinalIgnoreCase))
            arguments = arguments.Replace("/I", "/X", StringComparison.OrdinalIgnoreCase);

        var info = new ProcessStartInfo
        {
            FileName = file,
            Arguments = arguments,
            UseShellExecute = true,   // asi el desinstalador pide elevacion si su manifiesto la exige
        };
        using (var process = Process.Start(info))
        {
            if (process is not null)
                await process.WaitForExitAsync();
        }

        for (var i = 0; i < 20; i++)
        {
            if (!KeyExists(entry))
                return true;
            await Task.Delay(1000);
        }
        return !KeyExists(entry);
    }

    private static bool KeyExists(Win32Entry entry)
    {
        using var key = entry.Hive.OpenSubKey(entry.KeyPath);
        return key is not null;
    }

    /// <summary>«"C:\x\unins.exe" /arg» → (C:\x\unins.exe, /arg); sin comillas se corta tras el .exe.</summary>
    private static (string File, string Arguments) SplitCommand(string command)
    {
        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end > 1)
                return (text[1..end], text[(end + 1)..].Trim());
        }
        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0)
            return (text[..(exe + 4)], text[(exe + 4)..].Trim());
        var space = text.IndexOf(' ');
        return space > 0 ? (text[..space], text[(space + 1)..]) : (text, string.Empty);
    }

    // ------------------------------------------------------------------ MSIX (PackageManager)

    private static void ReadPackages(bool includeSystem, List<InstalledApp> result)
    {
        var manager = new PackageManager();
        var own = OwnPackageFamily();
        foreach (var package in manager.FindPackagesForUser(string.Empty))
        {
            try
            {
                if (package.IsFramework || package.IsResourcePackage || package.IsBundle)
                    continue;
                if (own is not null && package.Id.FamilyName == own)
                    continue;
                var isSystem = package.SignatureKind == global::Windows.ApplicationModel.PackageSignatureKind.System;
                if (isSystem && !includeSystem)
                    continue;
                string label;
                try { label = package.DisplayName; } catch (Exception) { label = package.Id.Name; }
                if (string.IsNullOrWhiteSpace(label) || label.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                    label = package.Id.Name;
                var v = package.Id.Version;
                DateTime installed;
                try { installed = package.InstalledDate.LocalDateTime; } catch (Exception) { installed = DateTime.MinValue; }
                ImageSource? icon = null;
                try
                {
                    var logo = package.Logo;
                    if (logo is not null && logo.IsFile && File.Exists(logo.LocalPath))
                        icon = ImageSource.FromFile(logo.LocalPath);
                }
                catch (Exception) { }
                string publisher;
                try { publisher = package.PublisherDisplayName; } catch (Exception) { publisher = string.Empty; }

                result.Add(new InstalledApp
                {
                    PackageName = MsixPrefix + package.Id.FullName,
                    Label = $"{label} {v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                    IsSystem = isSystem,
                    InstallDate = installed,
                    UpdatedDate = installed,
                    SizeBytes = 0,
                    Icon = icon,
                    Publisher = publisher,
                });
            }
            catch (Exception)
            {
                // Un paquete a medio instalar no debe tirar la lista.
            }
        }
    }

    private static async Task<bool> RemovePackageAsync(string fullName)
    {
        var manager = new PackageManager();
        var operation = manager.RemovePackageAsync(fullName);
        var outcome = await operation;
        if (outcome.ExtendedErrorCode is not null && outcome.ExtendedErrorCode.HResult != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(outcome.ErrorText) ? outcome.ExtendedErrorCode.Message : outcome.ErrorText);
        return manager.FindPackageForUser(string.Empty, fullName) is null;
    }

    /// <summary>Nombre de familia del propio paquete cuando se corre desde el MSIX; null en el exe suelto.</summary>
    private static string? OwnPackageFamily()
    {
        try
        {
            return global::Windows.ApplicationModel.Package.Current?.Id.FamilyName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
