using Microsoft.Extensions.Logging;
using Uninstaller.Helpers;
using Uninstaller.Services;

namespace Uninstaller;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        // Sin fuentes propias: se usa la tipografia del sistema (constitucion A.9).
        builder.UseMauiApp<App>();

#if DEBUG
        // Trazas de depuracion solo en Debug (constitucion 10).
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#else
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
#endif

        // Servicios (constitucion 5: inyeccion de dependencias para todos los servicios).
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
        builder.Services.AddSingleton<UpdateService>();
#if ANDROID
        builder.Services.AddSingleton<IAppInventoryService, Platforms.Android.AppInventoryService>();
        builder.Services.AddSingleton<IToastService, Platforms.Android.ToastService>();
#endif

        // Las paginas (carpeta Pages) las instancia el Shell por DataTemplate y resuelven sus
        // servicios via ServiceHelper (constitucion 7: code-behind delgado, sin ViewModels).

        var app = builder.Build();
        ServiceHelper.Initialize(app.Services);
        return app;
    }
}
