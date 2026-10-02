using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Uninstaller.Services;
using Uninstaller.Tests.Fakes;
using Uninstaller.ViewModels;

namespace Uninstaller.Tests;

[Collection("Cultura")]
public sealed class SettingsViewModelTests : ConIdioma
{
    private readonly FakeDesktop _desktop = new();
    private readonly SettingsViewModel _vm;

    public SettingsViewModelTests() => _vm = new SettingsViewModel(Settings, Loc, _desktop);

    [Fact]
    public void Textos_y_estado()
    {
        Settings.TrayOnMinimize = true;
        _desktop.StartsWithWindows = true;

        Assert.Equal(L("SettingsTitle"), _vm.Title);
        Assert.Equal($"🌐 {L("SettingsLanguage")}", _vm.LanguageTitle);
        Assert.False(_vm.IsSpanish);
        Assert.True(_vm.TrayOnMinimize);
        Assert.True(_vm.StartsWithWindows);
    }

    [Fact]
    public void Idioma_solo_cambia_si_es_otro()
    {
        Assert.False(_vm.SetLanguage("en"));
        Assert.True(_vm.SetLanguage("es"));
        Assert.Equal("es", Settings.Language);
        Assert.True(_vm.IsSpanish);
    }

    [Fact]
    public void Bandeja_y_arranque_se_guardan_salvo_mientras_carga()
    {
        _vm.Loading = true;
        _vm.SetTrayOnMinimize(true);
        _vm.SetStartWithWindows(true);
        Assert.False(Settings.TrayOnMinimize);
        Assert.False(_desktop.StartsWithWindows);
        Assert.Empty(_desktop.TrayChanges);

        _vm.Loading = false;
        _vm.SetTrayOnMinimize(true);
        _vm.SetStartWithWindows(true);
        Assert.True(Settings.TrayOnMinimize);
        Assert.Equal(new[] { true }, _desktop.TrayChanges);
        Assert.True(_desktop.StartsWithWindows);
    }
}

[Collection("Cultura")]
public sealed class AboutViewModelTests : ConIdioma
{
    private readonly FakeEnvironment _env = new() { VersionString = "2026.10.1.0" };
    private readonly AboutViewModel _vm;

    public AboutViewModelTests() => _vm = new AboutViewModel(Loc, _env, NullLogger.Instance);

    [Fact]
    public void Titulo_version_y_tarjetas()
    {
        Assert.Equal(L("AboutTitle"), _vm.Title);
        Assert.Equal(string.Format(L("AboutVersion"), "2026.10.1.0"), _vm.VersionText);
        Assert.Equal($"📧 {L("AboutContact")}", _vm.CardTitle("📧", "AboutContact"));
        Assert.Equal(4, AboutViewModel.CardTitles.Count);
    }

    [Fact]
    public async Task Contacto_por_correo()
    {
        await _vm.ContactAsync(Dialogs);
        Assert.Equal((L("EmailSubject"), AboutViewModel.ContactEmail), Assert.Single(_env.Emails));

        _env.EmailAvailable = false;
        await _vm.ContactAsync(Dialogs);
        Assert.Equal(L("ErrorEmailNotAvailable"), Dialogs.Last.Message);

        _env.EmailError = new InvalidOperationException("roto");
        await _vm.ContactAsync(Dialogs);
        Assert.Equal($"{L("ErrorEmail")}: roto", Dialogs.Last.Message);
    }
}

[Collection("Cultura")]
public sealed class UpdateServiceTests : ConIdioma
{
    private readonly FakeEnvironment _env = new() { VersionString = "2026.9.30.0" };

    private UpdateService Create(StubHttpHandler handler) => new(Loc, _env, new HttpClient(handler));

    [Theory]
    [InlineData("2026.10.1.0", "2026.9.30.0", 1)]
    [InlineData("2026.09.30.00", "2026.9.30.0", 0)]
    [InlineData("2026.9.30", "2026.9.30.1", -1)]
    [InlineData("abc", "0", 0)]
    public void Comparar_versiones_por_partes(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(UpdateService.CompareVersions(a, b)));

    [Fact]
    public async Task Version_nueva_aceptada_abre_el_enlace()
    {
        var handler = StubHttpHandler.Json("""{"version":"2026.10.1.0","url":"https://example.org/u"}""");
        Dialogs.Answer(true);

        await Create(handler).CheckAndPromptAsync(Dialogs);

        Assert.Equal(new Uri(UpdateService.AppcastUrl), Assert.Single(handler.Requests));
        Assert.Equal(string.Format(L("UpdateBody"), "2026.10.1.0", "2026.9.30.0"), Dialogs.Last.Message);
        Assert.Equal(L("UpdateTitle"), Dialogs.Last.Title);
        Assert.Equal(new Uri("https://example.org/u"), Assert.Single(_env.Opened));
    }

    [Fact]
    public async Task Rechazada_o_sin_enlace_no_abre_nada()
    {
        Dialogs.Answer(false);
        await Create(StubHttpHandler.Json("""{"version":"2027.1.1.0","url":"https://example.org"}""")).CheckAndPromptAsync(Dialogs);
        Dialogs.Answer(true);
        await Create(StubHttpHandler.Json("""{"version":"2027.1.1.0"}""")).CheckAndPromptAsync(Dialogs);
        Assert.Empty(_env.Opened);
        Assert.Equal(2, Dialogs.Calls.Count);
    }

    [Theory]
    [InlineData("""{"version":"2026.9.30.0"}""")]
    [InlineData("""{"url":"x"}""")]
    [InlineData("roto")]
    public async Task Igual_vieja_o_rota_no_dice_nada(string json)
    {
        await Create(StubHttpHandler.Json(json)).CheckAndPromptAsync(Dialogs);
        Assert.Empty(Dialogs.Calls);
    }

    [Fact]
    public async Task Sin_red_calla_y_solo_mira_una_vez()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var service = Create(handler);
        await service.CheckAndPromptAsync(Dialogs);
        await service.CheckAndPromptAsync(Dialogs);
        Assert.Single(handler.Requests);
        Assert.Empty(Dialogs.Calls);
        Assert.NotNull(new UpdateService(Loc, _env));
    }
}
