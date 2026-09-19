using Uninstaller.Services;

namespace Uninstaller.Platforms.Android;

/// <inheritdoc cref="IShellActions"/>
/// <remarks>En Android la utilidad de espacio no esta a la vista (haria falta MANAGE_EXTERNAL_STORAGE);
/// esta implementacion cubre la interfaz por si algun dia se abre.</remarks>
public class ShellActions : IShellActions
{
    public IReadOnlyList<DriveEntry> GetDrives()
    {
        var root = global::Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath;
        return root is null ? [] : [new DriveEntry(root, root, 0, 0, false)];
    }

    public void RevealInExplorer(string path) { }

    public bool MoveToRecycleBin(string path)
    {
        try { File.Delete(path); return true; } catch (Exception) { return false; }
    }
}
