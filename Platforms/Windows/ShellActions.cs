using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Uninstaller.Services;

namespace Uninstaller.Platforms.Windows;

/// <inheritdoc cref="IShellActions"/>
public class ShellActions : IShellActions
{
    public IReadOnlyList<DriveEntry> GetDrives()
    {
        var list = new List<DriveEntry>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady)
                    continue;
                var label = d.VolumeLabel.Length > 0 ? $"{d.Name} {d.VolumeLabel}" : d.Name;
                list.Add(new DriveEntry(d.RootDirectory.FullName, label, d.TotalSize, d.AvailableFreeSpace, d.DriveType == DriveType.Network));
            }
            catch (Exception)
            {
                // Unidad de red caida o lector vacio: fuera de la lista.
            }
        }
        return list;
    }

    public void RevealInExplorer(string path)
    {
        var info = Directory.Exists(path)
            ? new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            : new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"");
        info.UseShellExecute = true;
        Process.Start(info);
    }

    /// <summary>SHFileOperation con FOF_ALLOWUNDO: a la papelera, como desde el Explorador.</summary>
    /// <remarks>
    /// Dos trampas que hacian reventar la aplicacion: la estructura SHFILEOPSTRUCT solo va
    /// empaquetada (Pack = 1) en 32 bits; en x64 lleva el relleno normal, y con Pack = 1 shell32
    /// leia los punteros desplazados. Y el shell quiere un hilo STA: se le da uno propio en vez del
    /// del pool (MTA) desde el que llama la pagina.
    /// </remarks>
    public bool MoveToRecycleBin(string path) => MoveToRecycleBin([path]).Count == 0;

    public IReadOnlyList<string> MoveToRecycleBin(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return [];
        var thread = new Thread(() =>
        {
            try
            {
                var op = new SHFILEOPSTRUCT
                {
                    wFunc = 3,                              // FO_DELETE
                    // Varias rutas: separadas por un nulo y con doble nulo al final (una sola operacion).
                    pFrom = string.Join("\0", paths) + "\0\0",
                    fFlags = 0x0040 | 0x0010 | 0x0400,      // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI (el progreso lo enseña Windows; los errores los trata la pagina)
                };
                SHFileOperation(ref op);
            }
            catch (Exception) { }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        // Con varias rutas el resultado global no dice cuales fallaron: lo que sigue ahi, fallo.
        return paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
    }

    /// <summary>
    /// takeown + icacls sobre cada ruta (recursivo en las carpetas), en un cmd elevado (UAC). El
    /// usuario va por su SID (*S-1-…), que icacls acepta en cualquier idioma; la respuesta de takeown
    /// a su pregunta de confirmacion si que depende del idioma (S en español, Y en el resto).
    /// </summary>
    public async Task<bool> FixPermissionsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return true;
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null)
            return false;
        var yes = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "S" : "Y";
        var script = new StringBuilder("@echo off\r\n");
        foreach (var p in paths)
        {
            var quoted = "\"" + p.TrimEnd('\\') + "\"";
            // Dueño, fuera las denegaciones explicitas al usuario (ganan a cualquier permiso) y control total.
            if (Directory.Exists(p))
            {
                script.Append($"takeown /F {quoted} /R /D {yes} >nul 2>&1\r\n");
                script.Append($"icacls {quoted} /remove:d *{sid} /T /C /Q >nul 2>&1\r\n");
                script.Append($"icacls {quoted} /grant *{sid}:(OI)(CI)F /T /C /Q >nul 2>&1\r\n");
            }
            else
            {
                script.Append($"takeown /F {quoted} >nul 2>&1\r\n");
                script.Append($"icacls {quoted} /remove:d *{sid} /C /Q >nul 2>&1\r\n");
                script.Append($"icacls {quoted} /grant *{sid}:F /C /Q >nul 2>&1\r\n");
            }
        }
        var file = Path.Combine(Path.GetTempPath(), $"sOCUninstaller-permisos-{Guid.NewGuid():N}.cmd");
        await File.WriteAllTextAsync(file, script.ToString(), new UTF8Encoding(false));
        try
        {
            var info = new ProcessStartInfo("cmd.exe", $"/c \"{file}\"")
            {
                UseShellExecute = true,
                Verb = "runas",                       // UAC
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var process = Process.Start(info);
            if (process is null)
                return false;
            await process.WaitForExitAsync();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;                              // el usuario dijo que no en el UAC
        }
        finally
        {
            try { File.Delete(file); } catch (Exception) { }
        }
    }

    // ------------------------------------------------------------------ carpetas del sistema

    /// <summary>Hasta donde llega la proteccion de una carpeta: solo ella, ella y sus hijas directas, o todo lo que cuelga.</summary>
    private enum Reach { Exact, Children, Subtree }

    private static readonly Lazy<List<(string Path, string Key, Reach Reach)>> Protected = new(() =>
    {
        var list = new List<(string, string, Reach)>();
        void Add(string path, string key, Reach reach)
        {
            if (path.Length > 0) list.Add((path.TrimEnd('\\'), key, reach));
        }
        Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "DiskRiskWindows", Reach.Subtree);
        // Un programa entero (hija directa de Archivos de programa) o todo Archivos de programa; dentro de un programa ya no se avisa.
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DiskRiskPrograms", Reach.Children);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "DiskRiskPrograms", Reach.Children);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DiskRiskProgramData", Reach.Children);
        // La raiz de los perfiles (y cada perfil), el perfil del usuario y sus AppData: borrarlos deja la sesion inservible.
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0)
        {
            Add(Path.GetDirectoryName(profile) ?? string.Empty, "DiskRiskProfile", Reach.Children);
            Add(profile, "DiskRiskProfile", Reach.Exact);
            Add(Path.Combine(profile, "AppData"), "DiskRiskProfile", Reach.Exact);
            Add(Path.Combine(profile, "AppData", "Local"), "DiskRiskProfile", Reach.Exact);
            Add(Path.Combine(profile, "AppData", "Roaming"), "DiskRiskProfile", Reach.Exact);
            Add(Path.Combine(profile, "AppData", "LocalLow"), "DiskRiskProfile", Reach.Exact);
        }
        return list;
    });

    private static readonly string[] SystemNames = ["$Recycle.Bin", "System Volume Information", "Recovery", "Boot", "EFI", "PerfLogs", "hiberfil.sys", "pagefile.sys", "swapfile.sys", "bootmgr", "BOOTNXT"];

    public string? SystemRisk(string path)
    {
        var full = path.TrimEnd('\\');
        // Cosas de la raiz de cualquier unidad (papelera, restauracion, arranque, memoria virtual).
        if (Path.GetDirectoryName(full) is { } parent && Path.GetPathRoot(full) is { } root && string.Equals(parent.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            && SystemNames.Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase))
            return "DiskRiskSystem";
        foreach (var (protectedPath, key, reach) in Protected.Value)
        {
            if (string.Equals(full, protectedPath, StringComparison.OrdinalIgnoreCase))
                return key;
            if (reach == Reach.Exact || !full.StartsWith(protectedPath + "\\", StringComparison.OrdinalIgnoreCase))
                continue;
            if (reach == Reach.Subtree)
                return key;
            if (Path.GetDirectoryName(full) is { } dir && string.Equals(dir.TrimEnd('\\'), protectedPath, StringComparison.OrdinalIgnoreCase))
                return key;
        }
        return null;
    }

    // ------------------------------------------------------------------ iconos del sistema

    // Cada icono se guarda una vez como PNG en disco (por extension, «carpeta» o unidad) y a cada fila
    // se le da su propio ImageSource de fichero: un mismo ImageSource de flujo compartido entre muchas
    // filas de un CollectionView se pintaba a ratos si y a ratos no.
    private static readonly Dictionary<string, string?> IconFiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string IconDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCUninstaller", "icons");

    /// <summary>
    /// SHGetFileInfo por atributos (sin tocar el disco): un icono por extension y uno de carpeta.
    /// Para la raiz escaneada (una unidad, una carpeta conocida) se mira la ruta real, que tiene icono propio.
    /// </summary>
    public ImageSource? IconFor(string path, bool isFolder)
    {
        var key = isFolder ? (Path.GetDirectoryName(path) is null || path.Length <= 3 ? "drive:" + path : "folder") : Path.GetExtension(path).ToLowerInvariant();
        if (key.Length == 0) key = "file";
        string? file;
        lock (IconFiles)
        {
            if (!IconFiles.TryGetValue(key, out file))
            {
                file = LoadIcon(key, key.StartsWith("drive:") ? path : (isFolder ? "carpeta" : "x" + key), isFolder, byAttributes: !key.StartsWith("drive:"));
                IconFiles[key] = file;
            }
        }
        return file is null ? null : ImageSource.FromFile(file);
    }

    private static string? LoadIcon(string key, string path, bool isFolder, bool byAttributes)
    {
        try
        {
            var info = new SHFILEINFO();
            var flags = 0x100u | 0x1u;   // SHGFI_ICON | SHGFI_SMALLICON
            if (byAttributes) flags |= 0x10u;   // SHGFI_USEFILEATTRIBUTES
            var attributes = isFolder ? 0x10u : 0x80u;   // FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_NORMAL
            if (SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags) == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;
            try
            {
                using var icon = System.Drawing.Icon.FromHandle(info.hIcon);
                using var bitmap = icon.ToBitmap();
                Directory.CreateDirectory(IconDir);
                var safe = string.Concat(key.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
                var file = Path.Combine(IconDir, safe + "-" + Convert.ToHexString(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(key)))[..8] + ".png");
                bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                return file;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

    // Sin Pack: en x64 la estructura lleva el relleno por defecto (solo en x86 va con Pack = 1).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
}
