namespace Uninstaller.Services;

/// <inheritdoc cref="IShellActions"/>
/// <remarks>
/// Lo que se puede hacer sin un Explorador debajo: borrar directamente (no hay papelera), sin
/// iconos, nombres traducidos ni carpetas del sistema. La usa Android, donde la utilidad de espacio
/// no esta a la vista (haria falta MANAGE_EXTERNAL_STORAGE): cubre la interfaz por si algun dia se
/// abre. Las unidades las da cada plataforma.
/// </remarks>
public class PortableShellActions : IShellActions
{
    private readonly Func<string?> _storageRoot;

    public PortableShellActions(Func<string?> storageRoot) => _storageRoot = storageRoot;

    public IReadOnlyList<DriveEntry> GetDrives() => _storageRoot() is { } root ? [new DriveEntry(root, root, 0, 0, false)] : [];

    public void RevealInExplorer(string path) { }

    public bool MoveToRecycleBin(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public IReadOnlyList<string> MoveToRecycleBin(IReadOnlyList<string> paths) => paths.Where(p => !MoveToRecycleBin(p)).ToList();

    public IReadOnlyList<string> DeleteForever(IReadOnlyList<string> paths, IProgress<DeleteProgress>? progress, CancellationToken token)
    {
        FileDeleter.Delete(paths, progress, token);
        return paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
    }

    public Task<bool> FixPermissionsAsync(IReadOnlyList<string> paths) => Task.FromResult(false);

    public string? SystemRisk(string path) => null;

    public ImageSource? IconFor(string path, bool isFolder) => null;

    public bool IsInRecycleBin(string path) => false;

    public string? DisplayName(string path) => null;

    public bool IsRecycleBinFolder(string path) => false;

    public string? RecycleBinOwner(string path) => null;
}
