using Microsoft.Extensions.Logging;
using Uninstaller.Helpers;
using Uninstaller.Services;
using Uninstaller.ViewModels;

namespace Uninstaller.Pages;

/// <summary>Acerca de: enlace fino con <see cref="AboutViewModel"/>.</summary>
public partial class AboutPage : ContentPage
{
    private readonly ILocalizationService _l;
    private readonly AboutViewModel _vm;
    private readonly IDialogService _dialogs;

    public AboutPage()
    {
        InitializeComponent();
        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _vm = new AboutViewModel(_l, ServiceHelper.GetRequiredService<IAppEnvironment>(),
            ServiceHelper.GetRequiredService<ILogger<AboutPage>>());
        _dialogs = new PageDialogService(this);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Title = _vm.Title;
        PageTexts.Apply(this, AboutViewModel.StaticTexts, _l);
        foreach (var (name, symbol, key) in AboutViewModel.CardTitles)
            ((Label)FindByName(name)).Text = _vm.CardTitle(symbol, key);
        VersionLabel.Text = _vm.VersionText;
        ContactButton.Text = AboutViewModel.ContactEmail;
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("//MainPage");

    private async void OnContactEmailClicked(object? sender, EventArgs e) => await _vm.ContactAsync(_dialogs);
}
