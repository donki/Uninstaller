using System.ComponentModel;

namespace Uninstaller.Models;

/// <summary>
/// Una aplicacion instalada en el dispositivo (constitucion 5: modelo de datos sin logica de
/// presentacion). La construye el servicio de inventario especifico de Android.
/// </summary>
public class InstalledApp : INotifyPropertyChanged
{
    public required string PackageName { get; init; }

    public required string Label { get; init; }

    public bool IsSystem { get; init; }

    /// <summary>Fecha de primera instalacion (PackageInfo.FirstInstallTime).</summary>
    public DateTime InstallDate { get; init; }

    /// <summary>Fecha de la ultima actualizacion (PackageInfo.LastUpdateTime).</summary>
    public DateTime UpdatedDate { get; init; }

    /// <summary>Tamano en disco de los APK de la app: el base mas los splits del App Bundle.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Icono de la app, ya convertido a un origen de imagen de MAUI.</summary>
    public ImageSource? Icon { get; init; }

    private string _details = string.Empty;

    /// <summary>
    /// Linea de detalle de la fila (fechas y tamano) ya formateada y traducida. La compone la
    /// pagina: el modelo no sabe de idiomas ni de formatos (constitucion 5).
    /// </summary>
    public string Details
    {
        get => _details;
        set
        {
            if (_details == value)
                return;
            _details = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Details)));
        }
    }

    private bool _isSelected;

    /// <summary>Marcada por el usuario para desinstalacion masiva.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
