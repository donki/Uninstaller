using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Uninstaller.Models;
using Uninstaller.Services;
using Uninstaller.Tests.Fakes;
using Uninstaller.ViewModels;

namespace Uninstaller.Tests;

/// <summary>LocalizationService cambia la cultura por defecto del proceso: estas pruebas van en serie.</summary>
[CollectionDefinition("Cultura", DisableParallelization = true)]
public sealed class CulturaCollection;

/// <summary>Restaura la cultura por defecto que LocalizationService cambia al elegir idioma.</summary>
public abstract class ConIdioma : IDisposable
{
    private readonly CultureInfo? _culture = CultureInfo.DefaultThreadCurrentCulture;
    private readonly CultureInfo? _uiCulture = CultureInfo.DefaultThreadCurrentUICulture;

    protected readonly MemorySettings Settings = new();
    protected readonly LocalizationService Loc;
    protected readonly ScriptedDialogs Dialogs = new();
    protected readonly FakeToast Toast = new();

    protected ConIdioma() => Loc = new LocalizationService(Settings, NullLogger<LocalizationService>.Instance);

    protected string L(string key) => Loc[key];

    public virtual void Dispose()
    {
        CultureInfo.DefaultThreadCurrentCulture = _culture;
        CultureInfo.DefaultThreadCurrentUICulture = _uiCulture;
    }
}

[Collection("Cultura")]
public sealed class MainViewModelTests : ConIdioma
{
    private readonly FakeInventory _inventory = new();
    private readonly FakeMainView _view = new();
    private int _changes;

    public MainViewModelTests()
    {
        _inventory.Installed.Add(App("com.whatsapp", "WhatsApp", new DateTime(2026, 1, 10), new DateTime(2026, 9, 1), 50_000_000, quiet: true));
        _inventory.Installed.Add(App("org.mozilla.firefox", "Firefox", new DateTime(2026, 3, 5), new DateTime(2026, 3, 5), 200_000_000));
        _inventory.Installed.Add(App("com.spotify", "Spotify", new DateTime(2025, 12, 1), DateTime.MinValue, 120_000_000, quiet: true));
        _inventory.SystemApps.Add(App("com.android.settings", "Settings", new DateTime(2020, 1, 1), DateTime.MinValue, 0));
    }

    private static InstalledApp App(string package, string label, DateTime installed, DateTime updated, long size, bool quiet = false) =>
        new() { PackageName = package, Label = label, InstallDate = installed, UpdatedDate = updated, SizeBytes = size, SupportsUnattended = quiet };

    private MainViewModel Create(bool windows = false)
    {
        var vm = new MainViewModel(Loc, Settings, _inventory, Toast, Dialogs, _view, NullLogger.Instance, windows);
        vm.Changed += (_, _) => _changes++;
        return vm;
    }

    private static InstalledApp Get(MainViewModel vm, string label) => vm.Apps.Single(a => a.Label == label);

    [Fact]
    public async Task Al_aparecer_carga_una_sola_vez_y_ordena_por_instalacion()
    {
        var vm = Create();
        await vm.AppearingAsync();
        await vm.AppearingAsync();

        Assert.Equal(1, _inventory.Loads);
        Assert.True(vm.LoadedOnce);
        Assert.Equal(new[] { "Firefox", "WhatsApp", "Spotify" }, vm.Visible.Select(a => a.Label));
        Assert.Equal($"{string.Format(L("InstalledAppsCount"), 3)} · {L("SortInstall")}", vm.CountText);
        Assert.Equal(L("UninstallSelected"), vm.UninstallText);
        Assert.False(vm.UninstallEnabled);
        Assert.False(vm.LoadingVisible);
        Assert.False(vm.IsBusy);
        Assert.True(_changes > 0);
    }

    [Fact]
    public async Task Detalles_de_cada_fila_con_y_sin_actualizacion()
    {
        var vm = Create();
        await vm.LoadAppsAsync();

        var culture = Loc.CurrentCulture;
        var pattern = ListRules.CompactDatePattern(culture);
        Assert.Equal(string.Format(culture, L("AppDetails"), new DateTime(2026, 1, 10).ToString(pattern, culture),
            new DateTime(2026, 9, 1).ToString(pattern, culture), ListRules.AppSize(50_000_000, culture)), Get(vm, "WhatsApp").Details);
        Assert.Equal(string.Format(culture, L("AppDetailsNoUpdate"), new DateTime(2026, 3, 5).ToString(pattern, culture),
            ListRules.AppSize(200_000_000, culture)), Get(vm, "Firefox").Details);
        Assert.StartsWith(string.Format(culture, L("AppDetailsNoUpdate"), new DateTime(2025, 12, 1).ToString(pattern, culture), "").TrimEnd(), Get(vm, "Spotify").Details);
    }

    [Fact]
    public async Task Fecha_desconocida_se_pinta_con_raya()
    {
        _inventory.Installed.Add(App("x.y", "Raro", DateTime.MinValue, DateTime.MinValue, 0));
        var vm = Create();
        await vm.LoadAppsAsync();
        Assert.StartsWith(string.Format(L("AppDetailsNoUpdate"), "—", "").TrimEnd(), Get(vm, "Raro").Details);
    }

    [Fact]
    public async Task Error_al_cargar_se_explica_y_la_pantalla_sigue()
    {
        _inventory.LoadError = new InvalidOperationException("sin permiso");
        var vm = Create();

        await vm.LoadAppsAsync();

        Assert.Equal(string.Format(L("ErrorLoad"), "sin permiso"), Dialogs.Last.Message);
        Assert.False(vm.LoadingVisible);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Visible);
    }

    [Fact]
    public async Task Una_carga_en_marcha_no_lanza_otra()
    {
        var vm = Create();
        _inventory.LoadGate = new TaskCompletionSource();
        var first = vm.LoadAppsAsync();
        Assert.True(vm.IsBusy);
        Assert.True(vm.LoadingVisible);

        await vm.LoadAppsAsync();
        _inventory.LoadGate.SetResult();
        await first;

        Assert.Equal(1, _inventory.Loads);
    }

    [Fact]
    public async Task Una_sola_app_usa_el_singular()
    {
        _inventory.Installed.RemoveRange(1, 2);
        var vm = Create();
        await vm.LoadAppsAsync();
        Assert.StartsWith(L("OneInstalledApp"), vm.CountText);
    }

    [Theory]
    [InlineData("name", "SortName", new[] { "Firefox", "Spotify", "WhatsApp" })]
    [InlineData("size", "SortSize", new[] { "Firefox", "Spotify", "WhatsApp" })]
    [InlineData("updated", "SortUpdated", new[] { "WhatsApp", "Firefox", "Spotify" })]
    [InlineData("install", "SortInstall", new[] { "Firefox", "WhatsApp", "Spotify" })]
    public async Task Ordenar_por_cada_criterio_lo_guarda_y_lo_dice(string mode, string key, string[] order)
    {
        Settings.SortMode = "name";
        var vm = Create();
        await vm.LoadAppsAsync();
        var index = Array.IndexOf(MainViewModel.SortModes, mode);
        var label = mode == "name" ? $"✓ {L(key)}" : L(key);

        Dialogs.Answer(label);
        await vm.ChooseSortAsync();

        Assert.Equal(mode, Settings.SortMode);
        Assert.Equal(order, vm.Visible.Select(a => a.Label));
        Assert.EndsWith(L(key), vm.CountText);
        Assert.Equal(label, Dialogs.Last.Options[index]);
    }

    [Fact]
    public async Task Ordenar_cancelado_o_respuesta_rara_no_cambia_nada()
    {
        var vm = Create();
        await vm.LoadAppsAsync();

        await vm.ChooseSortAsync();
        Dialogs.Answer("");
        await vm.ChooseSortAsync();
        Dialogs.Answer("otra cosa");
        await vm.ChooseSortAsync();

        Assert.Equal("install", Settings.SortMode);
        Assert.Equal(L("SortBy"), Dialogs.Last.Title);
    }

    [Fact]
    public void SortModeName_desconocido_es_instalacion() =>
        Assert.Equal(L("SortInstall"), Create().SortModeName(null));

    [Fact]
    public async Task Buscar_por_nombre_o_por_paquete()
    {
        var vm = Create();
        await vm.LoadAppsAsync();

        vm.SetSearch("fire");
        Assert.Equal("Firefox", Assert.Single(vm.Visible).Label);

        vm.SetSearch("com.");
        Assert.Equal(2, vm.Visible.Count);
        Assert.StartsWith(string.Format(L("InstalledAppsCount"), 2), vm.CountText);

        vm.SetSearch(null);
        Assert.Equal(3, vm.Visible.Count);
        Assert.Equal(string.Empty, vm.Search);
    }

    [Fact]
    public async Task Marcar_todo_solo_marca_lo_visible_y_el_contador_cuenta_todas()
    {
        var vm = Create();
        await vm.LoadAppsAsync();
        vm.ToggleRow(Get(vm, "Spotify"));
        vm.SetSearch("fire");

        vm.SelectAllVisible();

        Assert.True(Get(vm, "Firefox").IsSelected);
        Assert.False(Get(vm, "WhatsApp").IsSelected);
        Assert.Equal(2, vm.SelectedCount);
        Assert.Contains(string.Format(L("SelectedCount"), 2), vm.CountText);
        Assert.Equal($"{L("UninstallSelected")} (2)", vm.UninstallText);
        Assert.True(vm.UninstallEnabled);

        vm.ClearSelection();
        Assert.Equal(0, vm.SelectedCount);
        Assert.False(vm.UninstallEnabled);
    }

    [Fact]
    public async Task Mostrar_las_del_sistema_lo_guarda_y_recarga()
    {
        var vm = Create();
        await vm.LoadAppsAsync();
        Assert.False(vm.ShowSystemApps);

        await vm.ToggleShowSystemAsync();

        Assert.True(Settings.ShowSystemApps);
        Assert.True(vm.ShowSystemApps);
        Assert.Equal(4, vm.Visible.Count);
        Assert.Equal(2, _inventory.Loads);
    }

    [Fact]
    public async Task La_lupa_abre_y_al_cerrar_vacia_el_filtro()
    {
        var vm = Create();
        await vm.LoadAppsAsync();

        vm.ToggleSearch();
        Assert.True(vm.SearchVisible);
        Assert.Equal(1, _view.Focused);
        vm.SetSearch("fire");

        vm.ToggleSearch();
        Assert.False(vm.SearchVisible);
        Assert.Equal(1, _view.Unfocused);
        Assert.Equal(string.Empty, vm.Search);
        Assert.Equal(3, vm.Visible.Count);
    }

    [Fact]
    public async Task Atras_cierra_primero_el_buscador_despues_la_seleccion()
    {
        var vm = Create();
        await vm.LoadAppsAsync();
        vm.ToggleRow(Get(vm, "Firefox"));
        vm.ToggleSearch();

        Assert.True(vm.HandleBack());
        Assert.False(vm.SearchVisible);
        Assert.Equal(1, vm.SelectedCount);

        Assert.True(vm.HandleBack());
        Assert.Equal(0, vm.SelectedCount);

        Assert.False(vm.HandleBack());
    }

    [Fact]
    public async Task Desinstalar_sin_nada_marcado_lo_dice()
    {
        var vm = Create();
        await vm.LoadAppsAsync();

        await vm.UninstallSelectedAsync();

        Assert.Equal(L("NothingSelected"), Dialogs.Last.Message);
        Assert.Empty(_inventory.Uninstalled);
    }

    [Fact]
    public async Task Desinstalar_cancelado_no_toca_nada()
    {
        var vm = Create();
        await vm.LoadAppsAsync();
        vm.SelectAllVisible();

        Dialogs.Answer(false);
        await vm.UninstallSelectedAsync();

        Assert.Equal(string.Format(L("ConfirmUninstallMany"), 3), Dialogs.Last.Message);
        Assert.Empty(_inventory.Uninstalled);
    }

    [Fact]
    public async Task Desinstalar_en_Android_una_tras_otra_con_progreso_y_resumen()
    {
        var vm = Create();
        await vm.LoadAppsAsync();
        vm.ToggleRow(Get(vm, "Firefox"));
        vm.ToggleRow(Get(vm, "Spotify"));
        _inventory.Refused.Add("com.spotify");

        Dialogs.Answer(true);
        await vm.UninstallSelectedAsync();

        // En Android no se pregunta por el modo desatendido.
        Assert.Single(Dialogs.Calls);
        Assert.Equal(new[] { ("org.mozilla.firefox", false), ("com.spotify", false) }, _inventory.Uninstalled);
        Assert.Equal(new[] { 0, 0, 0.5, 1 }, _view.Progress);
        Assert.Equal(string.Format(L("UninstallingItem"), 2, 2, "Spotify"), vm.ProgressDetail);
        Assert.False(vm.ProgressVisible);
        Assert.False(vm.LoadingVisible);
        Assert.Equal(new[] { string.Format(L("UninstallDone"), 1, 2) }, Toast.Shown);
        Assert.Equal(new[] { "WhatsApp", "Spotify" }, vm.Visible.Select(a => a.Label));
    }

    [Fact]
    public async Task Desinstalar_en_Windows_pregunta_si_desatendido_cuando_alguno_lo_admite()
    {
        var vm = Create(windows: true);
        await vm.LoadAppsAsync();
        vm.SelectAllVisible();

        Dialogs.Answer(true, true);
        await vm.UninstallSelectedAsync();

        Assert.Equal(string.Format(L("ConfirmUninstallManyWindows"), 3), Dialogs.Calls[0].Message);
        Assert.Equal(string.Format(L("UnattendedBody"), 2, 3), Dialogs.Calls[1].Message);
        Assert.All(_inventory.Uninstalled, u => Assert.True(u.Unattended));
        Assert.Empty(vm.Visible);
    }

    [Fact]
    public async Task Desinstalar_en_Windows_sin_ninguno_desatendible_no_pregunta()
    {
        var vm = Create(windows: true);
        await vm.LoadAppsAsync();
        vm.ToggleRow(Get(vm, "Firefox"));

        Dialogs.Answer(true);
        await vm.UninstallSelectedAsync();

        Assert.Single(Dialogs.Calls);
        Assert.False(Assert.Single(_inventory.Uninstalled).Unattended);
    }

    [Fact]
    public async Task Un_fallo_al_desinstalar_se_explica_y_sigue_con_las_demas()
    {
        var vm = Create();
        await vm.LoadAppsAsync();
        vm.SelectAllVisible();
        _inventory.Failing.Add("com.whatsapp");

        Dialogs.Answer(true);
        await vm.UninstallSelectedAsync();

        Assert.Contains(Dialogs.Messages, m => m == string.Format(L("ErrorUninstall"), "WhatsApp", "no se pudo com.whatsapp"));
        Assert.Equal(new[] { string.Format(L("UninstallDone"), 2, 3) }, Toast.Shown);
        Assert.Equal("WhatsApp", Assert.Single(vm.Visible).Label);
    }

    [Fact]
    public async Task Cambiar_de_idioma_rehace_los_textos()
    {
        var vm = Create();
        await vm.LoadAppsAsync();

        Loc.SetLanguage("es");
        vm.RefreshTexts();

        Assert.StartsWith(string.Format(L("InstalledAppsCount"), 3), vm.CountText);
        Assert.Equal(L("Loading"), vm.LoadingText);
        Assert.Equal(L("UninstallSelected"), vm.UninstallText);
    }

    [Fact]
    public void Textos_fijos_existen_en_los_dos_idiomas()
    {
        static Dictionary<string, string> Table(string name) =>
            (Dictionary<string, string>)typeof(LocalizationService)
                .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;

        var keys = MainViewModel.Descriptions.Values
            .Concat(MainViewModel.StaticTexts.Values)
            .Concat(SettingsViewModel.StaticTexts.Values)
            .Concat(AboutViewModel.StaticTexts.Values)
            .Concat(AboutViewModel.CardTitles.Select(c => c.Key))
            .Concat(DiskUsageViewModel.ButtonTexts.Values)
            .Concat(["SettingsTitle", "SettingsLanguage", "AboutTitle", "AboutVersion"]);
        foreach (var key in keys)
        {
            Assert.True(Table("English").ContainsKey(key), key);
            Assert.True(Table("Spanish").ContainsKey(key), key);
        }
    }
}
