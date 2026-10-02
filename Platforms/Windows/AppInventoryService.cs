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
    private const string MsixPrefix = "msix:";

    // Como desinstalar cada identificador de la ultima lista (la orden del registro o el nombre
    // completo del paquete): la pagina solo conoce el PackageName.
    private readonly Dictionary<string, Win32Entry> _win32 = new(StringComparer.OrdinalIgnoreCase);

    private sealed record Win32Entry(RegistryKey Hive, string KeyPath, string UninstallString, string? QuietUninstallString, InstallerKind Installer)
    {
        public bool SupportsUnattended => UninstallCommands.SupportsUnattended(QuietUninstallString, Installer);
    }

    public Task<IReadOnlyList<InstalledApp>> GetInstalledAppsAsync(bool includeSystem)
    {
        return Task.Run<IReadOnlyList<InstalledApp>>(() =>
        {
            var result = new List<InstalledApp>();
            _win32.Clear();
            var folders = new Dictionary<InstalledApp, string>();
            ReadRegistry(includeSystem, result, folders);
            ReadPackages(includeSystem, result, folders);
            MeasureFolders(folders);
            result.Sort((a, b) => b.InstallDate.CompareTo(a.InstallDate));
            return result;
        });
    }

    public async Task<bool> UninstallAsync(string packageName, bool unattended)
    {
        if (packageName.StartsWith(MsixPrefix, StringComparison.Ordinal))
            return await RemovePackageAsync(packageName[MsixPrefix.Length..]);

        if (_win32.TryGetValue(packageName, out var entry))
            return await RunUninstallerAsync(entry, unattended);

        throw new InvalidOperationException("Unknown program: " + packageName);
    }

    // ------------------------------------------------------------------ Win32 (registro)

    private static readonly (RegistryKey Hive, string Path)[] UninstallKeys =
    [
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
    ];

    private void ReadRegistry(bool includeSystem, List<InstalledApp> result, Dictionary<InstalledApp, string> folders)
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
                    var read = UninstallRegistry.Read(name, n => key.GetValue(n), includeSystem, seen,
                        UninstallRegistry.DetectInstaller, raw => ParseInstallDate(raw, key), ExtractIcon);
                    if (read is null)
                        continue;
                    _win32[read.App.PackageName] = new Win32Entry(hive, path + "\\" + name, read.UninstallString, read.QuietUninstallString, read.Installer);
                    result.Add(read.App);
                    if (read.FolderToMeasure is { } folder)
                        folders[read.App] = folder;
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
        if (UninstallCommands.ParseInstallDate(raw) is { } date)
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
    private static string? IconFile(string? value) => UninstallCommands.IconFile(value);

    /// <summary>
    /// Abre el desinstalador del fabricante (o msiexec) y espera a que acabe. Se da por
    /// desinstalado cuando su clave del registro ha desaparecido; algunos desinstaladores lanzan
    /// otro proceso y se cierran enseguida, asi que despues se le da un margen.
    /// </summary>
    private static async Task<bool> RunUninstallerAsync(Win32Entry entry, bool unattended)
    {
        var (file, arguments) = UninstallCommand(entry, unattended);

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

    /// <summary>
    /// La orden de desinstalar, con o sin preguntas. Desatendido: la QuietUninstallString del
    /// registro si la hay; si no, los modificadores de cada instalador (msiexec /qn, Inno Setup
    /// /VERYSILENT, NSIS /S). Los desconocidos van siempre con su asistente.
    /// </summary>
    private static (string File, string Arguments) UninstallCommand(Win32Entry entry, bool unattended) =>
        UninstallCommands.Build(entry.UninstallString, entry.QuietUninstallString, entry.Installer, unattended);

    private static bool KeyExists(Win32Entry entry)
    {
        using var key = entry.Hive.OpenSubKey(entry.KeyPath);
        return key is not null;
    }

    /// <summary>«"C:\x\unins.exe" /arg» → (C:\x\unins.exe, /arg); sin comillas se corta tras el .exe.</summary>
    private static (string File, string Arguments) SplitCommand(string command) => UninstallCommands.Split(command);

    // ------------------------------------------------------------------ MSIX (PackageManager)

    private static void MeasureFolders(Dictionary<InstalledApp, string> folders) => UninstallRegistry.MeasureFolders(folders);

    private static void ReadPackages(bool includeSystem, List<InstalledApp> result, Dictionary<InstalledApp, string> folders)
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

                var app = new InstalledApp
                {
                    PackageName = MsixPrefix + package.Id.FullName,
                    Label = $"{label} {v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                    IsSystem = isSystem,
                    InstallDate = installed,
                    UpdatedDate = installed,
                    SizeBytes = 0,
                    Icon = icon,
                    Publisher = publisher,
                    SupportsUnattended = true,
                };
                result.Add(app);
                // El tamaño real es lo que ocupa la carpeta del paquete (los MSIX no lo declaran).
                try
                {
                    var path = package.InstalledPath;
                    if (!string.IsNullOrEmpty(path))
                        folders[app] = path;
                }
                catch (Exception) { }
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
