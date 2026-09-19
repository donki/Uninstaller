using Uninstaller.Helpers;
using Uninstaller.Services;

namespace Uninstaller.Pages;

/// <summary>Ajustes de Windows: quedarse en la bandeja al minimizar y arrancar con Windows (en la bandeja).</summary>
public partial class SettingsPage : ContentPage
{
    private readonly ILocalizationService _l;
    private readonly ISettingsService _settings;
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _settings = ServiceHelper.GetRequiredService<ISettingsService>();
        _l.LanguageChanged += (_, _) => ApplyTexts();
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        _loading = true;
        Title = _l["SettingsTitle"];
        WindowsTitle.Text = _l["WindowsSection"];
        TrayLabel.Text = _l["TrayOnMinimize"];
        TrayHint.Text = _l["TrayOnMinimizeHint"];
        StartupLabel.Text = _l["StartWithWindows"];
        StartupHint.Text = _l["StartWithWindowsHint"];
        LanguageTitle.Text = $"🌐 {_l["SettingsLanguage"]}";
        LanguageHint.Text = _l["AboutLanguageHint"];
        var isSpanish = _l.CurrentLanguage == "es";
        SpanishButton.Style = (Style)Application.Current!.Resources[isSpanish ? "PrimaryButton" : "OutlineButton"];
        EnglishButton.Style = (Style)Application.Current.Resources[isSpanish ? "OutlineButton" : "PrimaryButton"];
        WindowsCard.IsVisible = DeviceInfo.Platform == DevicePlatform.WinUI;
        TraySwitch.IsToggled = _settings.TrayOnMinimize;
#if WINDOWS
        StartupSwitch.IsToggled = Platforms.Windows.WindowsStartup.IsEnabled("sOCUninstaller");
#endif
        _loading = false;
    }

    private void OnSpanishClicked(object? sender, EventArgs e) => SetLanguage("es");

    private void OnEnglishClicked(object? sender, EventArgs e) => SetLanguage("en");

    private void SetLanguage(string code)
    {
        if (code == _l.CurrentLanguage) return;
        _settings.Language = code;
        _l.SetLanguage(code);
    }

    private void OnTrayToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading) return;
        _settings.TrayOnMinimize = e.Value;
#if WINDOWS
        if (App.Tray is { } tray) tray.MinimizeToTray = e.Value;
#endif
    }

    private void OnStartupToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading) return;
#if WINDOWS
        Platforms.Windows.WindowsStartup.Set("sOCUninstaller", e.Value);
#endif
    }
}
