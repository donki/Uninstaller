namespace Uninstaller.Services;

/// <summary>Una unidad o raiz que se puede escanear: letra o ruta, etiqueta y, si se sabe, tamaño y libre.</summary>
public sealed record DriveEntry(string Path, string Label, long TotalBytes, long FreeBytes, bool IsNetwork);

/// <summary>
/// Lo que la utilidad de espacio en disco pide al sistema: las unidades, abrir una ruta en el
/// explorador del sistema y borrar a la papelera. La implementacion vive en Platforms/ (constitucion 5).
/// </summary>
public interface IShellActions
{
    IReadOnlyList<DriveEntry> GetDrives();

    /// <summary>Abre el Explorador (o equivalente) con el fichero o carpeta seleccionado.</summary>
    void RevealInExplorer(string path);

    /// <summary>Manda a la papelera (con deshacer). Devuelve false si el sistema no lo hizo.</summary>
    bool MoveToRecycleBin(string path);

    /// <summary>
    /// Manda varios a la papelera en una sola operacion (un solo dialogo de progreso y un solo
    /// «deshacer» en Windows). Devuelve los que NO se pudieron enviar (siguen existiendo).
    /// </summary>
    IReadOnlyList<string> MoveToRecycleBin(IReadOnlyList<string> paths);

    /// <summary>
    /// Hace al usuario dueño de esas rutas y le da control total sobre ellas y todo su contenido
    /// (Windows pide permiso de administrador). False si el usuario no lo concede o falla.
    /// </summary>
    Task<bool> FixPermissionsAsync(IReadOnlyList<string> paths);

    /// <summary>
    /// Si la ruta es (o cuelga de) una carpeta del sistema que es peligroso borrar, la clave del
    /// texto que lo explica; si no, null.
    /// </summary>
    string? SystemRisk(string path);

    /// <summary>Icono del sistema para una carpeta o un fichero (por su extension); null si la plataforma no los da.</summary>
    ImageSource? IconFor(string path, bool isFolder);

    /// <summary>
    /// Si la ruta ya esta dentro de la papelera: entonces no hay a donde mandarla y se borra
    /// definitivamente (la pagina lo avisa antes).
    /// </summary>
    bool IsInRecycleBin(string path);

    /// <summary>
    /// El nombre con el que el sistema ensena esa carpeta o fichero: «$Recycle.Bin» es «Papelera de
    /// reciclaje» y «Program Files», «Archivos de programa». Null si no hay uno distinto del de disco.
    /// </summary>
    string? DisplayName(string path);

    /// <summary>La papelera de una unidad (<c>X:\$Recycle.Bin</c>) o la de un usuario dentro de ella.</summary>
    bool IsRecycleBinFolder(string path);

    /// <summary>
    /// De quien es esa papelera: dentro de X:\$Recycle.Bin hay una carpeta por usuario, con su SID
    /// por nombre. Devuelve el nombre del usuario (o el SID si no se puede traducir); null si la ruta
    /// es la papelera de la unidad y no la de un usuario.
    /// </summary>
    string? RecycleBinOwner(string path);
}
