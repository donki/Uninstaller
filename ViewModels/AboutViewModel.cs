using Microsoft.Extensions.Logging;
using Uninstaller.Services;

namespace Uninstaller.ViewModels;

/// <summary>Logica de Acerca de: textos, version y contacto por correo.</summary>
public class AboutViewModel
{
    // CONFIGURACION (constantes identicas en todos los proyectos, constitucion A.9)
    public const string ContactEmail = "jsoladelarosa@gmail.com";

    /// <summary>Control de la pagina (x:Name) y clave del texto fijo que lleva.</summary>
    public static readonly IReadOnlyDictionary<string, string> StaticTexts = new Dictionary<string, string>
    {
        ["AppNameLabel"] = "AppName",
        ["DescriptionLabel"] = "AppDescription",
        ["CompanyLabel"] = "Company",
        ["ContactHint"] = "AboutContactHint",
        ["PrivacyText"] = "AboutPrivacyText",
        ["LicenseText"] = "AboutLicenseText",
        ["LegalText1"] = "AboutLegal1",
        ["LegalText2"] = "AboutLegal2",
        ["WarningText"] = "AboutWarning",
        ["BackButton"] = "Back",
    };

    /// <summary>Titulos de las tarjetas: control, simbolo delante y clave.</summary>
    public static readonly IReadOnlyList<(string Name, string Symbol, string Key)> CardTitles = new[]
    {
        ("ContactTitle", "📧", "AboutContact"),
        ("PrivacyTitle", "🔒", "AboutPrivacy"),
        ("LicenseTitle", "📄", "AboutLicense"),
        ("LegalTitle", "⚖️", "AboutLegal"),
    };

    private readonly ILocalizationService _l;
    private readonly IAppEnvironment _environment;
    private readonly ILogger _logger;

    public AboutViewModel(ILocalizationService localization, IAppEnvironment environment, ILogger logger)
    {
        _l = localization;
        _environment = environment;
        _logger = logger;
    }

    public string Title => _l["AboutTitle"];

    public string VersionText => string.Format(_l.CurrentCulture, _l["AboutVersion"], _environment.VersionString);

    public string CardTitle(string symbol, string key) => $"{symbol} {_l[key]}";

    /// <summary>Abre el correo al autor; si no se puede, lo explica.</summary>
    public async Task ContactAsync(IDialogService dialogs)
    {
        try
        {
            if (!await _environment.ComposeEmailAsync(_l["EmailSubject"], ContactEmail))
                await dialogs.AlertAsync(_l["Error"], _l["ErrorEmailNotAvailable"], _l["Ok"]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open the email client");
            await dialogs.AlertAsync(_l["Error"], $"{_l["ErrorEmail"]}: {ex.Message}", _l["Ok"]);
        }
    }
}
