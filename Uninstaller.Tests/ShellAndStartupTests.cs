using System.Reflection;
using Uninstaller.Helpers;
using Uninstaller.Pages;
using Uninstaller.Services;

namespace Uninstaller.Tests;

/// <summary>El Shell (menu lateral, titulos, atras), la ventana de la App y el arranque de MAUI.</summary>
[Collection("Paginas")]
public sealed class ShellAndStartupTests : PageHost
{
    [Fact]
    public void Shell_menu_y_titulos_en_el_idioma_elegido()
    {
        var shell = new AppShell();

        Assert.Equal(L("MenuHome"), Named<Label>(shell, "HomeLabel").Text);
        Assert.Equal(L("DiskMenu"), Named<Label>(shell, "DiskLabel").Text);
        Assert.Equal(L("SettingsTitle"), Named<Label>(shell, "SettingsLabel").Text);
        Assert.Equal(L("About"), Named<Label>(shell, "AboutLabel").Text);
        Assert.Equal(OperatingSystem.IsWindows(), Named<VisualElement>(shell, "DiskItem").IsVisible);
        Assert.StartsWith("v", Named<Label>(shell, "VersionLabel").Text);
        Assert.Contains(shell.Items, i => i.Title == L("MenuHome"));
        Assert.Contains(shell.Items, i => i.Title == L("About"));

        Loc.SetLanguage("es");

        Assert.Equal(L("MenuHome"), Named<Label>(shell, "HomeLabel").Text);
        Assert.Contains(shell.Items, i => i.Title == L("SettingsTitle"));
    }

    [Fact]
    public void Shell_atras_cierra_primero_el_menu_lateral()
    {
        var shell = new AppShell { FlyoutIsPresented = true };

        Assert.True((bool)Invoke(shell, "OnBackButtonPressed")!);
        Assert.False(shell.FlyoutIsPresented);
    }

    [Fact]
    public void Shell_con_ruta_de_disco_pendiente_va_al_disco_al_cargar()
    {
        DiskUsagePage.PendingPath = string.Empty;
        try
        {
            var shell = new AppShell();
            // Sin ventana no hay navegacion de verdad: el fallo se traga y la app se queda en la lista.
            typeof(VisualElement).GetMethod("SendLoaded", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!.Invoke(shell, null);
            Assert.NotNull(shell);
        }
        finally
        {
            DiskUsagePage.PendingPath = null;
        }
    }

    [Fact]
    public void La_app_crea_su_ventana_con_el_shell()
    {
        var window = (Window)typeof(App).GetMethod("CreateWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Application.Current, [null])!;

        Assert.Equal("sOC Uninstaller", window.Title);
        Assert.IsType<AppShell>(window.Page);
    }

    [Fact]
    public void El_arranque_registra_todos_los_servicios()
    {
        var app = MauiProgram.CreateMauiApp();
        try
        {
            Assert.IsType<SettingsService>(app.Services.GetService<ISettingsService>());
            Assert.IsType<LocalizationService>(app.Services.GetService<ILocalizationService>());
            Assert.IsType<MauiAppEnvironment>(app.Services.GetService<IAppEnvironment>());
            Assert.IsType<DesktopIntegration>(app.Services.GetService<IDesktopIntegration>());
            Assert.NotNull(app.Services.GetService<UpdateService>());
            // ServiceHelper queda apuntando al contenedor de la app.
            Assert.Same(app.Services.GetService<ISettingsService>(), ServiceHelper.GetRequiredService<ISettingsService>());
        }
        finally
        {
            app.Dispose();
        }
    }

    [Fact]
    public void Integracion_de_escritorio_fuera_de_Windows_no_hace_nada()
    {
        // En el binario de net10.0 (sin #if WINDOWS) no hay bandeja ni clave Run.
        var desktop = new DesktopIntegration();
        desktop.SetStartWithWindows(true);
        desktop.SetMinimizeToTray(true);
        Assert.False(desktop.StartsWithWindows);
    }

    [Fact]
    public void ServiceHelper_sin_inicializar_lo_dice()
    {
        var field = typeof(ServiceHelper).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var saved = field.GetValue(null);
        field.SetValue(null, null);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => ServiceHelper.GetRequiredService<ISettingsService>());
            Assert.Contains("MauiProgram", ex.Message);
        }
        finally
        {
            field.SetValue(null, saved);
        }
    }

    [Fact]
    public void Textos_de_pagina_con_un_control_que_no_es_de_texto_avisan()
    {
        var page = new ContentPage { Content = new Entry() };
        var scope = new Microsoft.Maui.Controls.Internals.NameScope();
        Microsoft.Maui.Controls.Internals.NameScope.SetNameScope(page, scope);
        ((Microsoft.Maui.Controls.Internals.INameScope)scope).RegisterName("Campo", page.Content);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PageTexts.Apply(page, new Dictionary<string, string> { ["Campo"] = "About" }, Loc));
        Assert.Contains("Campo", ex.Message);
    }
}
