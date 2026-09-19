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
    public bool MoveToRecycleBin(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = 3,                              // FO_DELETE
            pFrom = path + "\0\0",
            fFlags = 0x0040 | 0x0010,               // FOF_ALLOWUNDO | FOF_NOCONFIRMATION (con el dialogo de progreso de Windows)
        };
        var result = SHFileOperation(ref op);
        return result == 0 && !op.fAnyOperationsAborted;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
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
