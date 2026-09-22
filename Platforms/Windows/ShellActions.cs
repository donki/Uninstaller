using System.Diagnostics;
using System.Runtime.InteropServices;
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
                    fFlags = 0x0040 | 0x0010,               // FOF_ALLOWUNDO | FOF_NOCONFIRMATION (con el dialogo de progreso de Windows)
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
