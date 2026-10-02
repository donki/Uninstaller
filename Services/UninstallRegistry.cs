using System.Globalization;
using Uninstaller.Models;

namespace Uninstaller.Services;

/// <summary>Un programa leido de una clave Uninstall del registro, con lo necesario para quitarlo.</summary>
public sealed record UninstallEntry(InstalledApp App, string UninstallString, string? QuietUninstallString, InstallerKind Installer, string? FolderToMeasure);

/// <summary>
/// Reglas para convertir una clave <c>Uninstall</c> del registro de Windows en una fila de la lista,
/// y lo que se mira en disco para ello (marca del instalador, tamano de la carpeta). Sin registro:
/// los valores llegan por una funcion, asi que se prueba con un diccionario.
/// </summary>
public static class UninstallRegistry
{
    public const string Win32Prefix = "win32:";

    /// <summary>
    /// La fila de la clave <paramref name="keyName"/>, o null si no es un programa que se deba
    /// listar: sin nombre o sin orden de desinstalar, actualizaciones y parches, componentes del
    /// sistema (si no se piden) y repetidos (el mismo nombre y version en la clave de 64 y de 32 bits).
    /// </summary>
    public static UninstallEntry? Read(
        string keyName,
        Func<string, object?> value,
        bool includeSystem,
        ISet<string> seen,
        Func<string, InstallerKind> detectInstaller,
        Func<string?, DateTime> installDate,
        Func<string?, string, ImageSource?> icon)
    {
        var display = value("DisplayName") as string;
        var quiet = value("QuietUninstallString") as string;
        var uninstall = value("UninstallString") as string ?? quiet;
        if (string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(uninstall))
            return null;
        if (string.IsNullOrWhiteSpace(quiet))
            quiet = null;
        // Las actualizaciones (KB…) y los parches de MSI no son programas.
        if ((value("ParentKeyName") as string)?.Length > 0 || (value("ReleaseType") as string) is "Update" or "Hotfix" or "Security Update")
            return null;
        var isSystem = Convert.ToInt32(value("SystemComponent") ?? 0, CultureInfo.InvariantCulture) == 1;
        if (isSystem && !includeSystem)
            return null;
        // El mismo programa puede estar en la clave de 64 y en la de 32 bits.
        var version = value("DisplayVersion") as string ?? string.Empty;
        if (!seen.Add(display + "|" + version))
            return null;

        var isMsi = UninstallCommands.IsMsi(Convert.ToInt32(value("WindowsInstaller") ?? 0, CultureInfo.InvariantCulture), uninstall);
        var installer = isMsi ? InstallerKind.Msi : detectInstaller(uninstall);
        var installed = installDate(value("InstallDate") as string);
        var sizeKb = Convert.ToInt64(value("EstimatedSize") ?? 0L, CultureInfo.InvariantCulture);
        var location = value("InstallLocation") as string;
        var app = new InstalledApp
        {
            PackageName = Win32Prefix + keyName,
            Label = version.Length > 0 ? $"{display} {version}" : display,
            IsSystem = isSystem,
            InstallDate = installed,
            UpdatedDate = installed,
            SizeBytes = sizeKb * 1024,
            Icon = icon(value("DisplayIcon") as string, uninstall),
            Publisher = value("Publisher") as string ?? string.Empty,
            SupportsUnattended = UninstallCommands.SupportsUnattended(quiet, installer),
        };
        // Sin EstimatedSize, se mide la carpeta de instalacion (si la declara).
        var folder = sizeKb <= 0 && !string.IsNullOrWhiteSpace(location) ? location.Trim().Trim('"') : null;
        return new UninstallEntry(app, uninstall, quiet, installer, folder);
    }

    /// <summary>
    /// Inno Setup y NSIS se reconocen por una marca en el propio ejecutable de desinstalar (los dos
    /// la llevan en claro en su cabecera). Se mira solo el primer trozo del fichero.
    /// </summary>
    public static InstallerKind DetectInstaller(string uninstall)
    {
        try
        {
            var (file, _) = UninstallCommands.Split(uninstall);
            if (!File.Exists(file))
                return InstallerKind.Unknown;
            using var stream = File.OpenRead(file);
            var buffer = new byte[Math.Min(stream.Length, 2L * 1024 * 1024)];
            var read = stream.Read(buffer, 0, buffer.Length);
            var text = System.Text.Encoding.ASCII.GetString(buffer, 0, read);
            return UninstallCommands.DetectFromHeader(Path.GetFileName(file), text, File.Exists(Path.ChangeExtension(file, ".dat")));
        }
        catch (Exception)
        {
            // Sin acceso al fichero: se trata como desconocido y va con asistente.
        }
        return InstallerKind.Unknown;
    }

    /// <summary>
    /// Lo que ocupa en disco cada carpeta de instalacion, en paralelo: son cientos de carpetas y
    /// recorrerlas una detras de otra retrasaria la lista varios segundos.
    /// </summary>
    public static void MeasureFolders(IDictionary<InstalledApp, string> folders)
    {
        Parallel.ForEach(folders, new ParallelOptions { MaxDegreeOfParallelism = 8 }, pair =>
        {
            try
            {
                if (!Directory.Exists(pair.Value))
                    return;
                long total = 0;
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in new DirectoryInfo(pair.Value).EnumerateFiles("*", options))
                    total += file.Length;
                pair.Key.SizeBytes = total;
            }
            catch (Exception)
            {
                // Carpeta sin permiso o desaparecida: se queda sin tamano.
            }
        });
    }
}
