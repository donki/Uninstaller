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

    public IReadOnlyList<string> MoveToRecycleBin(IReadOnlyList<string> paths) => paths.Where(p => !MoveToRecycleBin(p)).ToList();

    public Task<bool> FixPermissionsAsync(IReadOnlyList<string> paths) => Task.FromResult(false);

    public string? SystemRisk(string path) => null;

    public ImageSource? IconFor(string path, bool isFolder) => null;

    public bool IsInRecycleBin(string path) => false;

    public string? DisplayName(string path) => null;

    public bool IsRecycleBinFolder(string path) => false;

    public string? RecycleBinOwner(string path) => null;
}
