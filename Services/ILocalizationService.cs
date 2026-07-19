namespace Uninstaller.Services;

/// <summary>
/// Acceso centralizado a los textos de la interfaz (constitucion 8).
/// Idiomas soportados: castellano e ingles. El ingles es el idioma por defecto
/// cuando el idioma del sistema no esta soportado.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Devuelve el texto de la clave indicada en el idioma activo.</summary>
    string this[string key] { get; }

    /// <summary>Codigo del idioma activo: "es" o "en".</summary>
    string CurrentLanguage { get; }

    /// <summary>Cultura activa, para formatear fechas y numeros.</summary>
    System.Globalization.CultureInfo CurrentCulture { get; }

    /// <summary>
    /// Fija el idioma. <c>null</c> o cadena vacia significa "seguir el idioma del sistema".
    /// </summary>
    void SetLanguage(string? languageCode);

    event EventHandler? LanguageChanged;
}
