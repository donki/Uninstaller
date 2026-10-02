using Uninstaller.Services;

namespace Uninstaller.ViewModels;

/// <summary>Logica de Ajustes: idioma y, en Windows, bandeja y arranque con el sistema.</summary>
public class SettingsViewModel
{
    /// <summary>Control de la pagina (x:Name) y clave del texto fijo que lleva.</summary>
    public static readonly IReadOnlyDictionary<string, string> StaticTexts = new Dictionary<string, string>
    {
        ["WindowsTitle"] = "WindowsSection",
        ["TrayLabel"] = "TrayOnMinimize",
        ["TrayHint"] = "TrayOnMinimizeHint",
        ["StartupLabel"] = "StartWithWindows",
        ["StartupHint"] = "StartWithWindowsHint",
        ["LanguageHint"] = "AboutLanguageHint",
    };

    private readonly ISettingsService _settings;
    private readonly ILocalizationService _l;
    private readonly IDesktopIntegration _desktop;

    public SettingsViewModel(ISettingsService settings, ILocalizationService localization, IDesktopIntegration desktop)
    {
        _settings = settings;
        _l = localization;
        _desktop = desktop;
    }

    /// <summary>Mientras la pagina rellena sus controles, sus eventos de cambio no deben guardar nada.</summary>
    public bool Loading { get; set; }

    public string Title => _l["SettingsTitle"];

    public string LanguageTitle => $"🌐 {_l["SettingsLanguage"]}";

    public bool IsSpanish => _l.CurrentLanguage == "es";

    public bool TrayOnMinimize => _settings.TrayOnMinimize;

    public bool StartsWithWindows => _desktop.StartsWithWindows;

    /// <summary>Cambia y guarda el idioma. True si ha cambiado.</summary>
    public bool SetLanguage(string code)
    {
        if (code == _l.CurrentLanguage)
            return false;
        _settings.Language = code;
        _l.SetLanguage(code);
        return true;
    }

    public void SetTrayOnMinimize(bool value)
    {
        if (Loading)
            return;
        _settings.TrayOnMinimize = value;
        _desktop.SetMinimizeToTray(value);
    }

    public void SetStartWithWindows(bool value)
    {
        if (!Loading)
            _desktop.SetStartWithWindows(value);
    }
}
