using Uninstaller.Helpers;
using Uninstaller.Services;
using Uninstaller.ViewModels;

namespace Uninstaller.Pages;

/// <summary>Ajustes: enlace fino con <see cref="SettingsViewModel"/>.</summary>
public partial class SettingsPage : ContentPage
{
    private readonly ILocalizationService _l;
    private readonly SettingsViewModel _vm;

    public SettingsPage()
    {
        InitializeComponent();
        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _vm = new SettingsViewModel(ServiceHelper.GetRequiredService<ISettingsService>(), _l,
            ServiceHelper.GetRequiredService<IDesktopIntegration>());
        _l.LanguageChanged += (_, _) => ApplyTexts();
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        _vm.Loading = true;
        Title = _vm.Title;
        PageTexts.Apply(this, SettingsViewModel.StaticTexts, _l);
        LanguageTitle.Text = _vm.LanguageTitle;
        SpanishButton.Style = (Style)Application.Current!.Resources[_vm.IsSpanish ? "PrimaryButton" : "OutlineButton"];
        EnglishButton.Style = (Style)Application.Current.Resources[_vm.IsSpanish ? "OutlineButton" : "PrimaryButton"];
        WindowsCard.IsVisible = DeviceInfo.Platform == DevicePlatform.WinUI;
        TraySwitch.IsToggled = _vm.TrayOnMinimize;
        StartupSwitch.IsToggled = _vm.StartsWithWindows;
        _vm.Loading = false;
    }

    private void OnSpanishClicked(object? sender, EventArgs e) => _vm.SetLanguage("es");

    private void OnEnglishClicked(object? sender, EventArgs e) => _vm.SetLanguage("en");

    private void OnTrayToggled(object? sender, ToggledEventArgs e) => _vm.SetTrayOnMinimize(e.Value);

    private void OnStartupToggled(object? sender, ToggledEventArgs e) => _vm.SetStartWithWindows(e.Value);
}
