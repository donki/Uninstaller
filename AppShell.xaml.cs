using Uninstaller.Helpers;
using Uninstaller.Services;

namespace Uninstaller;

public partial class AppShell : Shell
{
    private readonly ILocalizationService _l;

    public AppShell()
    {
        InitializeComponent();

        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _l.LanguageChanged += (_, _) => ApplyTexts();
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        HomeLabel.Text = _l["MenuHome"];
        AboutLabel.Text = _l["About"];

        // Los titulos de las rutas del Shell tambien se localizan (constitucion 8).
        foreach (var item in Items)
        {
            if (item.Route?.Contains("MainPage") == true)
                item.Title = _l["MenuHome"];
            else if (item.Route?.Contains("AboutPage") == true)
                item.Title = _l["About"];
        }
    }

    private async void OnHomeTapped(object sender, TappedEventArgs e) => await NavigateAsync("//MainPage");

    private async void OnAboutTapped(object sender, TappedEventArgs e) => await NavigateAsync("//AboutPage");

    private async Task NavigateAsync(string route)
    {
        FlyoutIsPresented = false;
        await GoToAsync(route);
    }
}
