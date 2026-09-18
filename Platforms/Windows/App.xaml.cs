using Microsoft.UI.Xaml;

namespace Uninstaller.WinUI;

/// <summary>Arranque WinUI de la misma aplicacion MAUI que corre en Android.</summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
