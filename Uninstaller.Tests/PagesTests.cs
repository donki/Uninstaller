using Uninstaller.Models;
using Uninstaller.Pages;

namespace Uninstaller.Tests;

/// <summary>Ajustes y Acerca de: las paginas de verdad, con sus controles.</summary>
[Collection("Paginas")]
public sealed class SettingsAndAboutPageTests : PageHost
{
    [Fact]
    public void Ajustes_vuelca_textos_y_estado()
    {
        Settings.TrayOnMinimize = true;
        Desktop.StartsWithWindows = true;

        var page = new SettingsPage();

        Assert.Equal(L("SettingsTitle"), page.Title);
        Assert.True(Named<Switch>(page, "TraySwitch").IsToggled);
        Assert.True(Named<Switch>(page, "StartupSwitch").IsToggled);
        Assert.Equal(OperatingSystem.IsWindows(), Named<Border>(page, "WindowsCard").IsVisible);
        // El idioma elegido (ingles) es el boton relleno; el otro, solo contorno.
        Assert.Same(Application.Current!.Resources["PrimaryButton"], Named<Button>(page, "EnglishButton").Style);
        Assert.Same(Application.Current.Resources["OutlineButton"], Named<Button>(page, "SpanishButton").Style);
    }

    [Fact]
    public void Ajustes_cambiar_idioma_repinta_la_pagina()
    {
        var page = new SettingsPage();

        Click(page, "SpanishButton");

        Assert.Equal("es", Settings.Language);
        Assert.Equal(L("SettingsTitle"), page.Title);
        Assert.Same(Application.Current!.Resources["PrimaryButton"], Named<Button>(page, "SpanishButton").Style);
    }

    [Fact]
    public void Ajustes_interruptores_guardan()
    {
        var page = new SettingsPage();

        Named<Switch>(page, "TraySwitch").IsToggled = true;
        Named<Switch>(page, "StartupSwitch").IsToggled = true;

        Assert.True(Settings.TrayOnMinimize);
        Assert.Equal([true], Desktop.TrayChanges);
        Assert.True(Desktop.StartsWithWindows);

        Click(page, "EnglishButton");   // ya era ingles: no cambia nada
        Assert.Equal("en", Settings.Language);
    }

    [Fact]
    public void AcercaDe_vuelca_version_y_contacto()
    {
        Environment.VersionString = "2026.11.24.0";
        var page = new AboutPage();

        Invoke(page, "OnAppearing");

        Assert.Equal(L("About"), page.Title);
        Assert.Contains("2026.11.24.0", Named<Label>(page, "VersionLabel").Text);
        Assert.Equal(ViewModels.AboutViewModel.ContactEmail, Named<Button>(page, "ContactButton").Text);
    }

    [Fact]
    public async Task AcercaDe_contacto_abre_el_correo()
    {
        var page = new AboutPage();
        Invoke(page, "OnAppearing");

        Click(page, "ContactButton");
        await Settle();

        Assert.Single(Environment.Emails);
        Assert.Equal(ViewModels.AboutViewModel.ContactEmail, Environment.Emails[0].To);
    }
}
