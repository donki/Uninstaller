using System.Globalization;

namespace Uninstaller.Services;

/// <summary>Que instalador hizo un programa de Windows: de eso depende como se le pide silencio.</summary>
public enum InstallerKind { Unknown, Msi, InnoSetup, Nsis }

/// <summary>
/// Lo que se lee y se decide con las entradas <c>Uninstall</c> del registro de Windows, sin tocar
/// el registro ni lanzar nada: partir la orden, reconocer el instalador, montar la orden
/// desatendida, sacar el icono y la fecha. El servicio de inventario de Windows lo usa; aqui vive
/// aparte para poder probarlo.
/// </summary>
public static class UninstallCommands
{
    /// <summary>«"C:\x\unins.exe" /arg» → (C:\x\unins.exe, /arg); sin comillas se corta tras el .exe.</summary>
    public static (string File, string Arguments) Split(string command)
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

    /// <summary>Si la entrada es de Windows Installer: la marca del registro o una orden con msiexec.</summary>
    public static bool IsMsi(int windowsInstallerFlag, string uninstall) =>
        windowsInstallerFlag == 1 || uninstall.Contains("msiexec", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Inno Setup y NSIS se reconocen por una marca en el propio ejecutable de desinstalar (los dos
    /// la llevan en claro en su cabecera), o Inno por su pareja «unins000.exe» + «unins000.dat».
    /// </summary>
    public static InstallerKind DetectFromHeader(string fileName, string header, bool hasDatBeside)
    {
        if (header.Contains("Inno Setup", StringComparison.Ordinal)
            || (fileName.StartsWith("unins", StringComparison.OrdinalIgnoreCase) && hasDatBeside))
            return InstallerKind.InnoSetup;
        if (header.Contains("Nullsoft", StringComparison.Ordinal) || header.Contains("NSIS Error", StringComparison.Ordinal))
            return InstallerKind.Nsis;
        return InstallerKind.Unknown;
    }

    /// <summary>Se puede quitar sin preguntas si el registro da la orden silenciosa o se conoce el instalador.</summary>
    public static bool SupportsUnattended(string? quietUninstall, InstallerKind kind) =>
        quietUninstall is not null || kind != InstallerKind.Unknown;

    /// <summary>
    /// La orden de desinstalar, con o sin preguntas. Desatendido: la QuietUninstallString del
    /// registro si la hay; si no, los modificadores de cada instalador (msiexec /qn, Inno Setup
    /// /VERYSILENT, NSIS /S). Los desconocidos van siempre con su asistente.
    /// </summary>
    public static (string File, string Arguments) Build(string uninstall, string? quietUninstall, InstallerKind kind, bool unattended)
    {
        if (unattended && quietUninstall is not null)
            return Split(quietUninstall);

        var (file, arguments) = Split(uninstall);
        if (kind == InstallerKind.Msi)
        {
            // «msiexec /I{…}» en UninstallString es «modificar»: para quitar hay que pedir /X.
            if (arguments.Contains("/I", StringComparison.OrdinalIgnoreCase) && !arguments.Contains("/X", StringComparison.OrdinalIgnoreCase))
                arguments = arguments.Replace("/I", "/X", StringComparison.OrdinalIgnoreCase);
            if (unattended)
                arguments += " /qn /norestart";
            return (file, arguments);
        }
        if (!unattended)
            return (file, arguments);
        return kind switch
        {
            InstallerKind.InnoSetup => (file, (arguments + " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART").Trim()),
            InstallerKind.Nsis => (file, (arguments + " /S").Trim()),
            _ => (file, arguments),
        };
    }

    /// <summary>
    /// Fichero del icono a partir de DisplayIcon o de la orden de desinstalar: quita comillas, el
    /// «,indice» y los argumentos. Null si no hay nada o es msiexec (su icono no es el del programa).
    /// </summary>
    public static string? IconFile(string? value)
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

    /// <summary>InstallDate del registro (AAAAMMDD); null si falta o no se entiende.</summary>
    public static DateTime? ParseInstallDate(string? raw) =>
        raw is { Length: 8 } && DateTime.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
