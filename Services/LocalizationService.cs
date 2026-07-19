using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Uninstaller.Services;

/// <inheritdoc cref="ILocalizationService"/>
public class LocalizationService : ILocalizationService
{
    public const string SystemLanguage = "";
    public const string DefaultLanguage = "en";

    private readonly ISettingsService _settings;
    private readonly ILogger<LocalizationService> _logger;
    private string _current = DefaultLanguage;

    public LocalizationService(ISettingsService settings, ILogger<LocalizationService> logger)
    {
        _settings = settings;
        _logger = logger;
        SetLanguage(_settings.Language);
    }

    public event EventHandler? LanguageChanged;

    public string CurrentLanguage => _current;

    public CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo(DefaultLanguage);

    public string this[string key]
    {
        get
        {
            var table = _current == "es" ? Spanish : English;
            if (table.TryGetValue(key, out var value))
                return value;

            if (English.TryGetValue(key, out var fallback))
            {
                _logger.LogWarning("Missing {Language} translation for key {Key}", _current, key);
                return fallback;
            }

            _logger.LogWarning("Unknown translation key {Key}", key);
            return key;
        }
    }

    public void SetLanguage(string? languageCode)
    {
        var resolved = Resolve(languageCode);
        if (resolved == _current && CurrentCulture is not null)
            return;

        _current = resolved;
        CurrentCulture = CultureInfo.GetCultureInfo(resolved);

        // Los formatos sensibles a la cultura siguen el idioma elegido (constitucion 8).
        CultureInfo.DefaultThreadCurrentCulture = CurrentCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CurrentCulture;

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resuelve el idioma efectivo: el elegido por el usuario si esta soportado; si se pide
    /// seguir al sistema, el del sistema cuando este soportado; en cualquier otro caso, ingles.
    /// </summary>
    private static string Resolve(string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
            return IsSupported(languageCode) ? languageCode : DefaultLanguage;

        try
        {
            var system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return IsSupported(system) ? system : DefaultLanguage;
        }
        catch (Exception)
        {
            return DefaultLanguage;
        }
    }

    private static bool IsSupported(string code) => code is "es" or "en";

    private static readonly Dictionary<string, string> English = new()
    {
        ["AppName"] = "Uninstaller",
        ["AppDescription"] = "Uninstall several apps at once",
        ["Company"] = "Socratic",

        // Menu y navegacion
        ["MenuHome"] = "Home",
        ["About"] = "About",

        // MainPage
        ["AppsTitle"] = "Installed apps",
        ["Loading"] = "Loading apps…",
        ["ShowSystemApps"] = "Show system apps",
        ["Refresh"] = "Refresh",
        ["SelectAll"] = "Select all",
        ["DeselectAll"] = "Clear selection",
        ["AppsCount"] = "{0} apps",
        ["OneApp"] = "1 app",
        ["SelectedCount"] = "{0} selected",
        ["SystemBadge"] = "System",
        ["UserBadge"] = "User",
        ["EmptyList"] = "No apps to show",
        ["EmptyListHint"] = "Pull to refresh or enable “Show system apps”.",
        ["UninstallSelected"] = "Uninstall selected",

        // Desinstalacion
        ["NothingSelected"] = "Select at least one app first.",
        ["ConfirmUninstallTitle"] = "Uninstall apps",
        ["ConfirmUninstallMany"] = "You are about to uninstall {0} apps. Android will ask you to confirm each one.",
        ["Continue"] = "Continue",
        ["Cancel"] = "Cancel",
        ["UninstallDone"] = "{0} of {1} apps uninstalled",

        // Comunes
        ["Ok"] = "OK",
        ["Close"] = "Close",
        ["Back"] = "← Back",
        ["Error"] = "Error",
        ["ErrorLoad"] = "The list of apps could not be loaded: {0}",
        ["ErrorUninstall"] = "“{0}” could not be uninstalled: {1}",
        ["ErrorNoActivity"] = "The uninstall screen could not be opened.",

        // Actualizacion (constitucion 15)
        ["UpdateTitle"] = "Update available",
        ["UpdateBody"] = "A newer version ({0}) is available. You have {1}.\nDo you want to update?",
        ["UpdateNow"] = "Update",
        ["UpdateLater"] = "Not now",

        // About
        ["AboutTitle"] = "About",
        ["AboutVersion"] = "Version {0}",
        ["AboutContact"] = "Contact",
        ["AboutContactHint"] = "Tap to send an email",
        ["SettingsLanguage"] = "Language",
        ["AboutLanguageHint"] = "Select your preferred language",
        ["AboutDonation"] = "Support Development",
        ["AboutDonationButton"] = "Ko-fi.com - Buy me a coffee",
        ["AboutDonationHint"] = "Your support helps maintain and improve the app",
        ["AboutLegal"] = "Legal Notice",
        ["AboutLegal1"] = "This software is provided 'as is', without warranty of any kind. The user is responsible for proper use of the app and compliance with local laws.",
        ["AboutLegal2"] = "In no event shall the authors be liable for any direct, indirect, incidental or consequential damages arising from the use of this software.",
        ["AboutWarning"] = "⚠️ Use at your own risk",
        ["AboutPrivacy"] = "Privacy",
        ["AboutPrivacyText"] = "This app reads the list of installed apps on your device only to show it to you. It does not collect your personal data or send anything to the developers.",
        ["AboutLicense"] = "License",
        ["AboutLicenseText"] = "This app is free software distributed under the MIT license.",
        ["EmailSubject"] = "Contact from Uninstaller",
        ["ErrorEmailNotAvailable"] = "No email app is available on this device.",
        ["ErrorEmail"] = "The email app could not be opened",
        ["BrowserNotAvailable"] = "Browser not available",
        ["LinkCopied"] = "The link was copied to the clipboard",
        ["ErrorBrowser"] = "The browser could not be opened"
    };

    private static readonly Dictionary<string, string> Spanish = new()
    {
        ["AppName"] = "Desinstalador",
        ["AppDescription"] = "Desinstala varias apps a la vez",
        ["Company"] = "Socratic",

        // Menu y navegacion
        ["MenuHome"] = "Inicio",
        ["About"] = "Acerca de",

        // MainPage
        ["AppsTitle"] = "Aplicaciones instaladas",
        ["Loading"] = "Cargando aplicaciones…",
        ["ShowSystemApps"] = "Mostrar apps del sistema",
        ["Refresh"] = "Actualizar",
        ["SelectAll"] = "Seleccionar todo",
        ["DeselectAll"] = "Quitar selección",
        ["AppsCount"] = "{0} aplicaciones",
        ["OneApp"] = "1 aplicación",
        ["SelectedCount"] = "{0} seleccionadas",
        ["SystemBadge"] = "Sistema",
        ["UserBadge"] = "Usuario",
        ["EmptyList"] = "No hay aplicaciones para mostrar",
        ["EmptyListHint"] = "Desliza para actualizar o activa «Mostrar apps del sistema».",
        ["UninstallSelected"] = "Desinstalar seleccionadas",

        // Desinstalacion
        ["NothingSelected"] = "Selecciona al menos una aplicación.",
        ["ConfirmUninstallTitle"] = "Desinstalar aplicaciones",
        ["ConfirmUninstallMany"] = "Vas a desinstalar {0} aplicaciones. Android te pedirá confirmación para cada una.",
        ["Continue"] = "Continuar",
        ["Cancel"] = "Cancelar",
        ["UninstallDone"] = "{0} de {1} aplicaciones desinstaladas",

        // Comunes
        ["Ok"] = "Aceptar",
        ["Close"] = "Cerrar",
        ["Back"] = "← Volver",
        ["Error"] = "Error",
        ["ErrorLoad"] = "No se ha podido cargar la lista de aplicaciones: {0}",
        ["ErrorUninstall"] = "No se ha podido desinstalar «{0}»: {1}",
        ["ErrorNoActivity"] = "No se ha podido abrir la pantalla de desinstalación.",

        // Actualizacion (constitucion 15)
        ["UpdateTitle"] = "Actualización disponible",
        ["UpdateBody"] = "Hay una versión más reciente ({0}). Tienes la {1}.\n¿Quieres actualizar?",
        ["UpdateNow"] = "Actualizar",
        ["UpdateLater"] = "Ahora no",

        // About
        ["AboutTitle"] = "Acerca de",
        ["AboutVersion"] = "Versión {0}",
        ["AboutContact"] = "Contacto",
        ["AboutContactHint"] = "Toca para enviar un correo electrónico",
        ["SettingsLanguage"] = "Idioma",
        ["AboutLanguageHint"] = "Selecciona tu idioma preferido",
        ["AboutDonation"] = "Apoya el Desarrollo",
        ["AboutDonationButton"] = "Ko-fi.com - Invítame un café",
        ["AboutDonationHint"] = "Tu apoyo ayuda a mantener y mejorar la aplicación",
        ["AboutLegal"] = "Aviso Legal",
        ["AboutLegal1"] = "Este software se proporciona «tal cual», sin garantías de ningún tipo. El usuario es responsable del uso adecuado de la aplicación y del cumplimiento de las leyes locales.",
        ["AboutLegal2"] = "En ningún caso los autores serán responsables de daños directos, indirectos, incidentales o consecuentes que resulten del uso de este software.",
        ["AboutWarning"] = "⚠️ Uso bajo su propio riesgo",
        ["AboutPrivacy"] = "Privacidad",
        ["AboutPrivacyText"] = "Esta aplicación lee la lista de apps instaladas en tu dispositivo solo para mostrártela. No recopila tus datos personales ni envía nada a los desarrolladores.",
        ["AboutLicense"] = "Licencia",
        ["AboutLicenseText"] = "Esta aplicación es software libre distribuido bajo licencia MIT.",
        ["EmailSubject"] = "Contacto desde Desinstalador",
        ["ErrorEmailNotAvailable"] = "No hay ninguna aplicación de correo disponible en este dispositivo.",
        ["ErrorEmail"] = "No se ha podido abrir la aplicación de correo",
        ["BrowserNotAvailable"] = "Navegador no disponible",
        ["LinkCopied"] = "El enlace se ha copiado al portapapeles",
        ["ErrorBrowser"] = "No se ha podido abrir el navegador"
    };
}
