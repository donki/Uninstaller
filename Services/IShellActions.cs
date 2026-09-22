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
}
