namespace Uninstaller.Services;

/// <summary>Hasta donde llega la proteccion de una carpeta: solo ella, ella y sus hijas directas, o todo lo que cuelga.</summary>
public enum ProtectionReach { Exact, Children, Subtree }

/// <summary>
/// Carpetas de Windows que no se deben borrar sin un aviso serio, y la clave del texto que lo
/// explica. Recibe las rutas del sistema en vez de preguntarlas, para poder probarlo con rutas
/// inventadas; <see cref="ForCurrentUser"/> las toma del equipo.
/// </summary>
public sealed class ProtectedFolders
{
    /// <summary>Lo que hay en la raiz de cualquier unidad y es del sistema (papelera, restauracion, arranque, memoria virtual).</summary>
    public static readonly string[] SystemNames =
        ["$Recycle.Bin", "System Volume Information", "Recovery", "Boot", "EFI", "PerfLogs", "hiberfil.sys", "pagefile.sys", "swapfile.sys", "bootmgr", "BOOTNXT"];

    private readonly List<(string Path, string Key, ProtectionReach Reach)> _list = [];

    public ProtectedFolders(string windows, string programFiles, string programFilesX86, string programData, string userProfile)
    {
        Add(windows, "DiskRiskWindows", ProtectionReach.Subtree);
        // Un programa entero (hija directa de Archivos de programa) o todo Archivos de programa; dentro de un programa ya no se avisa.
        Add(programFiles, "DiskRiskPrograms", ProtectionReach.Children);
        Add(programFilesX86, "DiskRiskPrograms", ProtectionReach.Children);
        Add(programData, "DiskRiskProgramData", ProtectionReach.Children);
        // La raiz de los perfiles (y cada perfil), el perfil del usuario y sus AppData: borrarlos deja la sesion inservible.
        if (userProfile.Length > 0)
        {
            Add(Path.GetDirectoryName(userProfile) ?? string.Empty, "DiskRiskProfile", ProtectionReach.Children);
            Add(userProfile, "DiskRiskProfile", ProtectionReach.Exact);
            Add(Path.Combine(userProfile, "AppData"), "DiskRiskProfile", ProtectionReach.Exact);
            Add(Path.Combine(userProfile, "AppData", "Local"), "DiskRiskProfile", ProtectionReach.Exact);
            Add(Path.Combine(userProfile, "AppData", "Roaming"), "DiskRiskProfile", ProtectionReach.Exact);
            Add(Path.Combine(userProfile, "AppData", "LocalLow"), "DiskRiskProfile", ProtectionReach.Exact);
        }
    }

    /// <summary>Las del equipo y el usuario actuales.</summary>
    public static ProtectedFolders ForCurrentUser() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private void Add(string path, string key, ProtectionReach reach)
    {
        if (path.Length > 0)
            _list.Add((path.TrimEnd('\\'), key, reach));
    }

    /// <summary>Clave del aviso si la ruta es del sistema; null si se puede borrar sin mas.</summary>
    public string? Risk(string path)
    {
        var full = path.TrimEnd('\\');
        if (Path.GetDirectoryName(full) is { } parent && Path.GetPathRoot(full) is { } root
            && string.Equals(parent.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            && SystemNames.Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase))
            return "DiskRiskSystem";
        foreach (var (protectedPath, key, reach) in _list)
        {
            if (string.Equals(full, protectedPath, StringComparison.OrdinalIgnoreCase))
                return key;
            if (reach == ProtectionReach.Exact || !full.StartsWith(protectedPath + "\\", StringComparison.OrdinalIgnoreCase))
                continue;
            if (reach == ProtectionReach.Subtree)
                return key;
            if (Path.GetDirectoryName(full) is { } dir && string.Equals(dir.TrimEnd('\\'), protectedPath, StringComparison.OrdinalIgnoreCase))
                return key;
        }
        return null;
    }
}
