namespace Uninstaller.Services;

/// <inheritdoc cref="ISettingsService"/>
public class SettingsService : ISettingsService
{
    private const string LanguageKey = "language";
    private const string ShowSystemKey = "show_system_apps";

    public string Language
    {
        get => Preferences.Get(LanguageKey, LocalizationService.SystemLanguage);
        set => Preferences.Set(LanguageKey, value ?? LocalizationService.SystemLanguage);
    }

    public bool ShowSystemApps
    {
        get => Preferences.Get(ShowSystemKey, false);
        set => Preferences.Set(ShowSystemKey, value);
    }
}
