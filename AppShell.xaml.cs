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
        // Arrancado con --disk: directo al espacio en disco (la ruta, si vino, la escanea la pagina).
        if (Pages.DiskUsagePage.PendingPath is not null)
        {
            Loaded += (_, _) => Dispatcher.Dispatch(async () =>
            {
                try { await GoToAsync("//DiskUsagePage"); }
                catch (Exception) { /* sin la pagina se queda en la lista, que no es grave */ }
            });
        }
    }

    private void ApplyTexts()
    {
        HomeLabel.Text = _l["MenuHome"];
        DiskLabel.Text = _l["DiskMenu"];
        DiskItem.IsVisible = DeviceInfo.Platform == DevicePlatform.WinUI;
        AboutLabel.Text = _l["About"];
        VersionLabel.Text = $"v{AppInfo.Current.VersionString}";

        // Los titulos de las rutas del Shell tambien se localizan (constitucion 8).
        foreach (var item in Items)
        {
            if (item.Route?.Contains("MainPage") == true)
                item.Title = _l["MenuHome"];
            else if (item.Route?.Contains("DiskUsagePage") == true)
                item.Title = _l["DiskMenu"];
            else if (item.Route?.Contains("AboutPage") == true)
                item.Title = _l["About"];
        }
    }

    private async void OnHomeTapped(object sender, TappedEventArgs e) => await NavigateAsync("//MainPage");

    private async void OnDiskTapped(object sender, TappedEventArgs e) => await NavigateAsync("//DiskUsagePage");

    private async void OnAboutTapped(object sender, TappedEventArgs e) => await NavigateAsync("//AboutPage");

    private async Task NavigateAsync(string route)
    {
        FlyoutIsPresented = false;
        await GoToAsync(route);
    }
}
