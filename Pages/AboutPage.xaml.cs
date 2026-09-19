using Microsoft.Extensions.Logging;
using SocShared;
using Uninstaller.Helpers;
using Uninstaller.Services;

namespace Uninstaller.Pages;

public partial class AboutPage : ContentPage
{
    // CONFIGURACION (constantes idénticas en todos los proyectos, constitucion A.9)
    private const string ContactEmail = "jsoladelarosa@gmail.com";

    private readonly ILocalizationService _l;
    private readonly ISettingsService _settings;
    private readonly ILogger<AboutPage> _logger;

    public AboutPage()
    {
        InitializeComponent();

        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _settings = ServiceHelper.GetRequiredService<ISettingsService>();
        _logger = ServiceHelper.GetRequiredService<ILogger<AboutPage>>();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        Title = _l["AboutTitle"];

        AppNameLabel.Text = _l["AppName"];
        VersionLabel.Text = string.Format(_l.CurrentCulture, _l["AboutVersion"], AppInfo.Current.VersionString);
        DescriptionLabel.Text = _l["AppDescription"];
        CompanyLabel.Text = _l["Company"];

        ContactTitle.Text = $"📧 {_l["AboutContact"]}";
        ContactButton.Text = ContactEmail;
        ContactHint.Text = _l["AboutContactHint"];



        PrivacyTitle.Text = $"🔒 {_l["AboutPrivacy"]}";
        PrivacyText.Text = _l["AboutPrivacyText"];

        LicenseTitle.Text = $"📄 {_l["AboutLicense"]}";
        LicenseText.Text = _l["AboutLicenseText"];

        LegalTitle.Text = $"⚖️ {_l["AboutLegal"]}";
        LegalText1.Text = _l["AboutLegal1"];
        LegalText2.Text = _l["AboutLegal2"];
        WarningText.Text = _l["AboutWarning"];

        BackButton.Text = _l["Back"];
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("//MainPage");

    private async void OnContactEmailClicked(object? sender, EventArgs e)
    {
        try
        {
            var message = new EmailMessage
            {
                Subject = _l["EmailSubject"],
                To = new List<string> { ContactEmail }
            };

            await Email.Default.ComposeAsync(message);
        }
        catch (FeatureNotSupportedException)
        {
            await ModernDialog.AlertAsync(this, _l["Error"], _l["ErrorEmailNotAvailable"], _l["Ok"]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open the email client");
            await ModernDialog.AlertAsync(this, _l["Error"], $"{_l["ErrorEmail"]}: {ex.Message}", _l["Ok"]);
        }
    }

}
