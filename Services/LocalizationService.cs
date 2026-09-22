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
        ["Uninstalling"] = "Uninstalling…",
        ["UninstallingItem"] = "{0} of {1}: {2}",
        ["ShowSystemApps"] = "Show system apps",
        ["Refresh"] = "Refresh",
        ["SearchPlaceholder"] = "Search by name or package",
        ["SelectAll"] = "Select all",
        ["DeselectAll"] = "Clear selection",
        ["AppsCount"] = "{0} apps",
        ["OneApp"] = "1 app",
        ["InstalledAppsCount"] = "{0} installed apps",
        ["OneInstalledApp"] = "1 installed app",
        ["SelectedCount"] = "{0} selected",
        ["SystemBadge"] = "System",
        ["UserBadge"] = "User",
        ["EmptyList"] = "No apps to show",
        ["EmptyListHint"] = "Pull to refresh or enable “Show system apps”.",
        ["UninstallSelected"] = "Uninstall selected",

        // Ordenacion
        ["SortBy"] = "Sort by",
        ["SortInstall"] = "Install date",
        ["SortName"] = "Name (A–Z)",
        ["SortUpdated"] = "Last updated",
        ["SortSize"] = "Size",
        ["SortedBy"] = "by {0}",

        // Detalle de cada fila: {0} instalacion, {1} actualizacion, {2} tamano
        ["AppDetails"] = "Inst. {0} · Upd. {1} · {2}",
        ["AppDetailsNoUpdate"] = "Inst. {0} · {1}",

        // Desinstalacion
        ["NothingSelected"] = "Select at least one app first.",
        ["ConfirmUninstallTitle"] = "Uninstall apps",
        ["ConfirmUninstallMany"] = "You are about to uninstall {0} apps. Android will ask you to confirm each one.",
        ["ConfirmUninstallManyWindows"] = "You are about to uninstall {0} programs, one after another. Each one opens its own uninstaller (and may ask for permission); Store apps are removed directly.",
        ["UnattendedTitle"] = "Unattended?",
        ["UnattendedBody"] = "{0} of {1} can be uninstalled without questions (Windows Installer, Inno Setup, NSIS and Store apps). Unattended runs them silently; the rest open their wizard as usual. Windows may still ask for administrator permission.",
        ["Unattended"] = "Unattended",
        ["TrayOpen"] = "Open",
        ["SettingsTitle"] = "Settings",
        ["WindowsSection"] = "Windows",
        ["TrayOnMinimize"] = "Keep in the notification area when minimised",
        ["TrayOnMinimizeHint"] = "Click the tray icon to bring it back; right-click for Open or Exit.",
        ["StartWithWindows"] = "Start with Windows",
        ["StartWithWindowsHint"] = "Starts hidden in the notification area when you sign in.",
        ["DiskTitle"] = "Disk space",
        ["DiskMenu"] = "Disk space",
        ["DiskPathPlaceholder"] = @"Drive, folder or network path (\\server\share)",
        ["DiskHint"] = @"Pick a drive or type a path and press Scan. Network paths (\\server\share) work too.",
        ["DiskEmpty"] = "Nothing scanned yet.",
        ["DiskScan"] = "Scan",
        ["DiskStop"] = "Stop",
        ["DiskTree"] = "Folder tree",
        ["DiskLargest"] = "Largest files",
        ["DiskTypes"] = "By file type",
        ["DiskAge"] = "By age",
        ["DiskDuplicates"] = "Duplicate files",
        ["DiskMap"] = "Treemap (one tap selects, two open the folder)",
        ["DiskUp"] = "Up one level",
        ["DiskOpen"] = "Show in Explorer",
        ["DiskCopy"] = "Copy path",
        ["DiskDelete"] = "Move to Recycle Bin",
        ["DiskExport"] = "Export to CSV",
        ["DiskScanning"] = "Scanning… {0} folders, {1} files, {2} · {3}",
        ["DiskDone"] = "{0}: {1} in {2} files and {3} folders ({4} s)",
        ["DiskCancelled"] = "Scan cancelled.",
        ["DiskScanError"] = "Could not scan {0}: {1}",
        ["DiskFolderDetail"] = "{0} % · {1} files · {2} folders · {3}",
        ["DiskInaccessible"] = "some folders could not be read",
        ["DiskAggregateDetail"] = "{0} % · {1} files",
        ["DiskNoExtension"] = "(no extension)",
        ["DiskAge1"] = "Modified in the last month",
        ["DiskAge2"] = "1 to 6 months",
        ["DiskAge3"] = "6 months to a year",
        ["DiskAge4"] = "1 to 2 years",
        ["DiskAge5"] = "Older than 2 years",
        ["DiskHashing"] = "Comparing files… {0} of {1} · {2}",
        ["DiskDuplicateTitle"] = "{0} · {1} copies · {2} each",
        ["DiskDuplicateDetail"] = "{0} recoverable by keeping one copy",
        ["DiskDuplicatesDone"] = "{0} groups of duplicates, {1} recoverable",
        ["DiskCopied"] = "Path copied",
        ["DiskDeleteFileConfirm"] = "Move this file to the Recycle Bin?" + Environment.NewLine + "{0}",
        ["DiskDeleteFolderConfirm"] = "Move this folder and everything in it to the Recycle Bin?" + Environment.NewLine + "{0}",
        ["DiskDeleteFailed"] = "Could not move {0} to the Recycle Bin.",
        ["DiskDeleted"] = "Moved to the Recycle Bin",
        ["DiskDeleteMany"] = "Move {0} checked items to the Recycle Bin",
        ["DiskDeleteManyConfirm"] = "Move these {0} items ({1}) to the Recycle Bin?" + Environment.NewLine + "{2}",
        ["DiskAndMore"] = "… and {0} more",
        ["DiskDeletingMany"] = "Moving {0} items to the Recycle Bin…",
        ["DiskDeletedMany"] = "{0} items moved to the Recycle Bin",
        ["DiskDeleteFailedMany"] = "{0} items could not be moved to the Recycle Bin:" + Environment.NewLine + "{1}",
        ["DiskCopiedMany"] = "{0} paths copied",
        ["DiskRiskTitle"] = "System folder",
        ["DiskRiskBody"] = "Deleting this can leave Windows, your programs or your user account unusable:" + Environment.NewLine + "{0}" + Environment.NewLine + Environment.NewLine + "Continue anyway?",
        ["DiskRiskContinue"] = "Continue anyway",
        ["DiskRiskWindows"] = "part of Windows",
        ["DiskRiskPrograms"] = "an installed program (uninstall it from Programs instead)",
        ["DiskRiskProgramData"] = "shared program data",
        ["DiskRiskProfile"] = "your user profile",
        ["DiskRiskSystem"] = "reserved by the system (Recycle Bin, restore points, boot, virtual memory)",
        ["DiskPermissionsTitle"] = "No permission",
        ["DiskPermissionsBody"] = "You do not have permission to delete this:" + Environment.NewLine + "{0}" + Environment.NewLine + Environment.NewLine + "Take ownership and grant yourself full control over it and everything inside, then try again? Windows will ask for administrator permission.",
        ["DiskPermissionsFix"] = "Change permissions and retry",
        ["DiskPermissionsWorking"] = "Changing permissions…",
        ["DiskPermissionsDenied"] = "Permissions were not changed",
        ["DiskExported"] = "Exported to {0}",
        ["DiskDeleting"] = "Moving to the Recycle Bin… {0}",
        ["DiskExporting"] = "Exporting… {0}",
        ["TrayExit"] = "Exit",
        ["WithWizard"] = "With wizard",
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
        ["Uninstalling"] = "Desinstalando…",
        ["UninstallingItem"] = "{0} de {1}: {2}",
        ["ShowSystemApps"] = "Mostrar apps del sistema",
        ["Refresh"] = "Actualizar",
        ["SearchPlaceholder"] = "Buscar por nombre o paquete",
        ["SelectAll"] = "Seleccionar todo",
        ["DeselectAll"] = "Quitar selección",
        ["AppsCount"] = "{0} aplicaciones",
        ["OneApp"] = "1 aplicación",
        ["InstalledAppsCount"] = "{0} aplicaciones instaladas",
        ["OneInstalledApp"] = "1 aplicación instalada",
        ["SelectedCount"] = "{0} seleccionadas",
        ["SystemBadge"] = "Sistema",
        ["UserBadge"] = "Usuario",
        ["EmptyList"] = "No hay aplicaciones para mostrar",
        ["EmptyListHint"] = "Desliza para actualizar o activa «Mostrar apps del sistema».",
        ["UninstallSelected"] = "Desinstalar seleccionadas",

        // Ordenacion
        ["SortBy"] = "Ordenar por",
        ["SortInstall"] = "Fecha de instalación",
        ["SortName"] = "Nombre (A–Z)",
        ["SortUpdated"] = "Última actualización",
        ["SortSize"] = "Tamaño",
        ["SortedBy"] = "por {0}",

        // Detalle de cada fila: {0} instalacion, {1} actualizacion, {2} tamano
        ["AppDetails"] = "Inst. {0} · Act. {1} · {2}",
        ["AppDetailsNoUpdate"] = "Inst. {0} · {1}",

        // Desinstalacion
        ["NothingSelected"] = "Selecciona al menos una aplicación.",
        ["ConfirmUninstallTitle"] = "Desinstalar aplicaciones",
        ["ConfirmUninstallMany"] = "Vas a desinstalar {0} aplicaciones. Android te pedirá confirmación para cada una.",
        ["ConfirmUninstallManyWindows"] = "Vas a desinstalar {0} programas, uno detrás de otro. Cada uno abre su propio desinstalador (y puede pedir permiso); las apps de la Store se quitan directamente.",
        ["UnattendedTitle"] = "¿Desatendido?",
        ["UnattendedBody"] = "{0} de {1} se pueden desinstalar sin preguntas (Windows Installer, Inno Setup, NSIS y apps de la Store). En desatendido van en silencio; el resto abre su asistente como siempre. Windows puede pedir permiso de administrador igualmente.",
        ["Unattended"] = "Desatendido",
        ["TrayOpen"] = "Abrir",
        ["SettingsTitle"] = "Ajustes",
        ["WindowsSection"] = "Windows",
        ["TrayOnMinimize"] = "Quedarse en el área de notificación al minimizar",
        ["TrayOnMinimizeHint"] = "Clic en el icono de la bandeja para volver; botón derecho para Abrir o Salir.",
        ["StartWithWindows"] = "Arrancar con Windows",
        ["StartWithWindowsHint"] = "Arranca escondido en el área de notificación al iniciar sesión.",
        ["DiskTitle"] = "Espacio en disco",
        ["DiskMenu"] = "Espacio en disco",
        ["DiskPathPlaceholder"] = @"Unidad, carpeta o ruta de red (\\servidor\recurso)",
        ["DiskHint"] = @"Elige una unidad o escribe una ruta y pulsa Escanear. Las rutas de red (\\servidor\recurso) también valen.",
        ["DiskEmpty"] = "Todavía no se ha escaneado nada.",
        ["DiskScan"] = "Escanear",
        ["DiskStop"] = "Parar",
        ["DiskTree"] = "Árbol de carpetas",
        ["DiskLargest"] = "Ficheros más grandes",
        ["DiskTypes"] = "Por tipo de fichero",
        ["DiskAge"] = "Por antigüedad",
        ["DiskDuplicates"] = "Ficheros duplicados",
        ["DiskMap"] = "Mapa de rectángulos (un toque elige, dos entran en la carpeta)",
        ["DiskUp"] = "Subir un nivel",
        ["DiskOpen"] = "Ver en el Explorador",
        ["DiskCopy"] = "Copiar ruta",
        ["DiskDelete"] = "Enviar a la papelera",
        ["DiskExport"] = "Exportar a CSV",
        ["DiskScanning"] = "Escaneando… {0} carpetas, {1} ficheros, {2} · {3}",
        ["DiskDone"] = "{0}: {1} en {2} ficheros y {3} carpetas ({4} s)",
        ["DiskCancelled"] = "Escaneo cancelado.",
        ["DiskScanError"] = "No se ha podido escanear {0}: {1}",
        ["DiskFolderDetail"] = "{0} % · {1} ficheros · {2} carpetas · {3}",
        ["DiskInaccessible"] = "alguna carpeta no se pudo leer",
        ["DiskAggregateDetail"] = "{0} % · {1} ficheros",
        ["DiskNoExtension"] = "(sin extensión)",
        ["DiskAge1"] = "Modificados el último mes",
        ["DiskAge2"] = "De 1 a 6 meses",
        ["DiskAge3"] = "De 6 meses a un año",
        ["DiskAge4"] = "De 1 a 2 años",
        ["DiskAge5"] = "Más de 2 años",
        ["DiskHashing"] = "Comparando ficheros… {0} de {1} · {2}",
        ["DiskDuplicateTitle"] = "{0} · {1} copias · {2} cada una",
        ["DiskDuplicateDetail"] = "{0} recuperables dejando una sola copia",
        ["DiskDuplicatesDone"] = "{0} grupos de duplicados, {1} recuperables",
        ["DiskCopied"] = "Ruta copiada",
        ["DiskDeleteFileConfirm"] = "¿Enviar este fichero a la papelera?" + Environment.NewLine + "{0}",
        ["DiskDeleteFolderConfirm"] = "¿Enviar esta carpeta y todo lo que contiene a la papelera?" + Environment.NewLine + "{0}",
        ["DiskDeleteFailed"] = "No se ha podido enviar {0} a la papelera.",
        ["DiskDeleted"] = "Enviado a la papelera",
        ["DiskDeleteMany"] = "Enviar los {0} elementos marcados a la papelera",
        ["DiskDeleteManyConfirm"] = "¿Enviar estos {0} elementos ({1}) a la papelera?" + Environment.NewLine + "{2}",
        ["DiskAndMore"] = "… y {0} más",
        ["DiskDeletingMany"] = "Enviando {0} elementos a la papelera…",
        ["DiskDeletedMany"] = "{0} elementos enviados a la papelera",
        ["DiskDeleteFailedMany"] = "No se han podido enviar {0} elementos a la papelera:" + Environment.NewLine + "{1}",
        ["DiskCopiedMany"] = "{0} rutas copiadas",
        ["DiskRiskTitle"] = "Carpeta del sistema",
        ["DiskRiskBody"] = "Borrar esto puede dejar Windows, tus programas o tu cuenta de usuario inservibles:" + Environment.NewLine + "{0}" + Environment.NewLine + Environment.NewLine + "¿Seguir de todos modos?",
        ["DiskRiskContinue"] = "Seguir de todos modos",
        ["DiskRiskWindows"] = "forma parte de Windows",
        ["DiskRiskPrograms"] = "un programa instalado (desinstálalo desde Programas)",
        ["DiskRiskProgramData"] = "datos compartidos de programas",
        ["DiskRiskProfile"] = "tu perfil de usuario",
        ["DiskRiskSystem"] = "reservado por el sistema (papelera, puntos de restauración, arranque, memoria virtual)",
        ["DiskPermissionsTitle"] = "Sin permisos",
        ["DiskPermissionsBody"] = "No tienes permisos para borrar esto:" + Environment.NewLine + "{0}" + Environment.NewLine + Environment.NewLine + "¿Hacerte dueño y darte control total sobre ello y todo su contenido, y volver a intentarlo? Windows pedirá permiso de administrador.",
        ["DiskPermissionsFix"] = "Cambiar permisos y reintentar",
        ["DiskPermissionsWorking"] = "Cambiando permisos…",
        ["DiskPermissionsDenied"] = "No se han cambiado los permisos",
        ["DiskExported"] = "Exportado a {0}",
        ["DiskDeleting"] = "Enviando a la papelera… {0}",
        ["DiskExporting"] = "Exportando… {0}",
        ["TrayExit"] = "Salir",
        ["WithWizard"] = "Con asistente",
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
