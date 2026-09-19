namespace Uninstaller.Services;

/// <inheritdoc cref="ISettingsService"/>
public class SettingsService : ISettingsService
{
    private const string LanguageKey = "language";
    private const string ShowSystemKey = "show_system_apps";
    private const string SortKey = "sort_mode";

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

    public bool TrayOnMinimize
    {
        get => Preferences.Get("tray_on_minimize", true);
        set => Preferences.Set("tray_on_minimize", value);
    }

    public string SortMode
    {
        get => Preferences.Get(SortKey, "install");
        set => Preferences.Set(SortKey, value ?? "install");
    }
}
