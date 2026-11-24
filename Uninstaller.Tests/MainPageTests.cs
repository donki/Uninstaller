using Uninstaller.Models;
using Uninstaller.Pages;

namespace Uninstaller.Tests;

/// <summary>La lista de aplicaciones de verdad: cargar, buscar, marcar y desinstalar pulsando sus controles.</summary>
[Collection("Paginas")]
public sealed class MainPageTests : PageHost
{
    private MainPage _page = null!;

    private async Task<MainPage> OpenAsync(params string[] labels)
    {
        var n = 0;
        foreach (var label in labels)
            Inventory.Installed.Add(new InstalledApp
            {
                PackageName = "pkg." + label.ToLowerInvariant(), Label = label, InstallDate = new DateTime(2026, 1, 1).AddDays(n++),
                SupportsUnattended = true,
            });
        _page = new MainPage();
        Invoke(_page, "OnAppearing");
        await Settle();
        return _page;
    }

    private List<InstalledApp> Rows => ((IEnumerable<InstalledApp>)Named<CollectionView>(_page, "AppsList").ItemsSource).ToList();

    [Fact]
    public async Task Al_aparecer_carga_la_lista_y_los_textos()
    {
        await OpenAsync("Alfa", "Beta", "Gamma");

        Assert.Equal(1, Inventory.Loads);
        Assert.Equal(3, Rows.Count);
        Assert.Equal(L("AppName"), _page.Title);
        Assert.False(string.IsNullOrEmpty(Named<Label>(_page, "CountLabel").Text));
        Assert.False(Named<Button>(_page, "UninstallButton").IsEnabled);
        Assert.False(Named<Grid>(_page, "LoadingOverlay").IsVisible);
        // El boton del disco solo existe en la compilacion de Windows (#if WINDOWS); aqui es net10.0.
        Assert.False(Named<VisualElement>(_page, "DiskButton").IsVisible);
        Assert.Equal(L("SearchPlaceholder"), Named<Entry>(_page, "SearchEntry").Placeholder);
    }

    [Fact]
    public async Task Buscar_filtra_y_atras_cierra_el_buscador()
    {
        await OpenAsync("Alfa", "Beta", "Gamma");

        Click(_page, "SearchButton");
        Assert.True(Named<View>(_page, "SearchRow").IsVisible);

        Named<Entry>(_page, "SearchEntry").Text = "bet";
        Assert.Equal(["Beta"], Rows.Select(r => r.Label));

        // Atras primero pliega el buscador (y vacia el texto) en lugar de salir.
        Assert.True((bool)Invoke(_page, "OnBackButtonPressed")!);
        Assert.False(Named<View>(_page, "SearchRow").IsVisible);
        Assert.Equal(string.Empty, Named<Entry>(_page, "SearchEntry").Text);
        Assert.Equal(3, Rows.Count);
    }

    [Fact]
    public async Task Marcar_todo_y_quitar_la_seleccion()
    {
        await OpenAsync("Alfa", "Beta");

        Click(_page, "SelectAllButton");
        Assert.True(Rows.All(r => r.IsSelected));
        Assert.True(Named<Button>(_page, "UninstallButton").IsEnabled);

        Click(_page, "ClearButton");
        Assert.True(Rows.All(r => !r.IsSelected));
        Assert.False(Named<Button>(_page, "UninstallButton").IsEnabled);
    }

    [Fact]
    public async Task Tocar_una_fila_la_marca()
    {
        await OpenAsync("Alfa", "Beta");
        var row = new Grid { BindingContext = Rows[1] };

        Invoke(_page, "OnRowTapped", row, null);
        Invoke(_page, "OnItemCheckedChanged", row, new CheckedChangedEventArgs(true));

        Assert.True(Rows[1].IsSelected);
        Assert.Contains("1", Named<Button>(_page, "UninstallButton").Text);
    }

    [Fact]
    public async Task Apps_del_sistema_se_enseñan_y_el_boton_se_marca()
    {
        await OpenAsync("Alfa");
        Inventory.SystemApps.Add(new InstalledApp { PackageName = "sys.core", Label = "Core", IsSystem = true });

        Click(_page, "ShowSystemButton");
        await Settle();

        Assert.True(Settings.ShowSystemApps);
        Assert.Equal(2, Rows.Count);
        Assert.Equal(Application.Current!.Resources["Primary"], Named<VisualElement>(_page, "ShowSystemButton").BackgroundColor);
    }

    [Fact]
    public async Task Actualizar_vuelve_a_pedir_la_lista()
    {
        await OpenAsync("Alfa");

        Click(_page, "RefreshButton");
        await Settle();
        Named<RefreshView>(_page, "ListRefresh").IsRefreshing = true;
        await Settle();

        Assert.Equal(3, Inventory.Loads);
        Assert.False(Named<RefreshView>(_page, "ListRefresh").IsRefreshing);
    }

    [Fact]
    public async Task Ordenar_por_nombre_desde_el_dialogo()
    {
        Settings.SortMode = "install";
        await OpenAsync("Gamma", "Alfa", "Beta");

        Click(_page, "SortButton");
        await AnswerDialog(_page, L("SortName"));

        Assert.Equal("name", Settings.SortMode);
        Assert.Equal(["Alfa", "Beta", "Gamma"], Rows.Select(r => r.Label));
    }

    [Fact]
    public async Task Desinstalar_sin_marcar_avisa()
    {
        await OpenAsync("Alfa");

        Invoke(_page, "OnUninstallSelectedClicked", null, EventArgs.Empty);
        var text = await AnswerDialog(_page, L("Ok"));

        Assert.Contains(L("NothingSelected"), text);
        Assert.Empty(Inventory.Uninstalled);
    }

    [Fact]
    public async Task Desinstalar_lo_marcado_tras_confirmar()
    {
        await OpenAsync("Alfa", "Beta");
        Rows.Single(r => r.Label == "Alfa").IsSelected = true;
        Invoke(_page, "OnItemCheckedChanged", null, new CheckedChangedEventArgs(true));

        Click(_page, "UninstallButton");
        await AnswerDialog(_page, L("Continue"));
        if (OperatingSystem.IsWindows())
            await AnswerDialog(_page, L("Unattended"));
        await Settle();

        Assert.Equal(["pkg.alfa"], Inventory.Uninstalled.Select(u => u.Package));
        Assert.Equal(OperatingSystem.IsWindows(), Inventory.Uninstalled[0].Unattended);
        Assert.Equal(["Beta"], Rows.Select(r => r.Label));
        Assert.False(Named<Grid>(_page, "LoadingOverlay").IsVisible);
    }

    [Fact]
    public async Task Desinstalar_cancelado_no_toca_nada()
    {
        await OpenAsync("Alfa");
        Click(_page, "SelectAllButton");

        Click(_page, "UninstallButton");
        await AnswerDialog(_page, L("Cancel"));

        Assert.Empty(Inventory.Uninstalled);
        Assert.False(DialogOpen(_page));
    }

    [Fact]
    public async Task Cambiar_de_idioma_repinta_los_textos()
    {
        await OpenAsync("Alfa");

        Loc.SetLanguage("es");

        Assert.Equal(L("AppName"), _page.Title);
        Assert.Equal(L("SearchPlaceholder"), Named<Entry>(_page, "SearchEntry").Placeholder);
    }
}
