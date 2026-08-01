namespace Uninstaller.Services;

/// <summary>
/// Preferencias ligeras del usuario, almacenadas en el dispositivo (constitucion 9).
/// </summary>
public interface ISettingsService
{
    /// <summary>Idioma elegido: "es", "en" o cadena vacia para seguir al sistema.</summary>
    string Language { get; set; }

    /// <summary>Si se muestran tambien las apps del sistema, ademas de las del usuario.</summary>
    bool ShowSystemApps { get; set; }

    /// <summary>Criterio de orden de la lista: "install" (defecto), "name" o "updated".</summary>
    string SortMode { get; set; }
}
