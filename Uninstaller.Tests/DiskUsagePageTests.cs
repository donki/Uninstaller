using System.Reflection;
using Uninstaller.Models;
using Uninstaller.Pages;
using Uninstaller.Services;
using Uninstaller.ViewModels;

namespace Uninstaller.Tests;

/// <summary>
/// El espacio en disco de verdad sobre un arbol temporal: escribir la ruta, escanear, cambiar de vista,
/// marcar y borrar pulsando los controles de la pagina.
/// </summary>
[Collection("Paginas")]
public sealed class DiskUsagePageTests : PageHost
{
    private readonly TempTree _tree = new();
    private readonly DiskUsagePage _page;

    public DiskUsagePageTests()
    {
        _tree.File(@"Fotos\a.jpg", 70_000, new DateTime(2020, 1, 1));
        _tree.File(@"Fotos\b.jpg", 70_000, new DateTime(2026, 9, 1), fill: 2);
        _tree.File(@"Fotos\Viejas\c.jpg", 70_000, new DateTime(2010, 1, 1));   // igual que a.jpg
        _tree.File(@"Docs\informe.pdf", 1000, DateTime.Now);
        _tree.File(@"suelto.bin", 500, DateTime.Now);
        Shell.Drives.Add(new DriveEntry(_tree.Root, "Prueba", 1000L << 30, 400L << 30, false));
        Shell.Drives.Add(new DriveEntry(@"\\nas\share", "nas", 0, 0, true));
        DiskUsagePage.PendingPath = null;
        DiskUsagePage.PendingMap = false;
        _page = new DiskUsagePage();
    }

    public override void Dispose()
    {
        DiskUsagePage.PendingPath = null;
        DiskUsagePage.PendingMap = false;
        _tree.Dispose();
        base.Dispose();
    }

    private DiskUsageViewModel Vm =>
        (DiskUsageViewModel)typeof(DiskUsagePage).GetField("_vm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_page)!;

    private string P(string relative) => Path.Combine(_tree.Root, relative);

    /// <summary>Espera a que termine el escaneo que lanzo el boton (va en segundo plano).</summary>
    private async Task WaitScanAsync()
    {
        for (var i = 0; i < 300 && (Vm.IsScanning || Vm.Root is null); i++)
            await Task.Delay(10);
        await Settle();
        Assert.NotNull(Vm.Root);
    }

    private async Task ScanAsync()
    {
        Named<Entry>(_page, "PathEntry").Text = _tree.Root;
        Click(_page, "ScanButton");
        await WaitScanAsync();
    }

    [Fact]
    public void Al_abrir_pone_textos_y_unidades()
    {
        Assert.Equal(L("DiskTitle"), _page.Title);
        Assert.Equal(L("DiskPathPlaceholder"), Named<Entry>(_page, "PathEntry").Placeholder);
        Assert.Equal(L("DiskEmpty"), Named<Label>(_page, "EmptyLabel").Text);
        var picker = Named<Picker>(_page, "DrivePicker");
        Assert.Equal(2, picker.ItemsSource.Count);
        Assert.Equal(0, picker.SelectedIndex);
        Assert.Equal(_tree.Root, Named<Entry>(_page, "PathEntry").Text);
        Assert.Equal(L("DiskScan"), ToolTipProperties.GetText(Named<Button>(_page, "ScanButton")));
    }

    [Fact]
    public void Elegir_otra_unidad_cambia_la_ruta()
    {
        Named<Picker>(_page, "DrivePicker").SelectedIndex = 1;
        Assert.Equal(@"\\nas\share", Named<Entry>(_page, "PathEntry").Text);
    }

    [Fact]
    public async Task Escanear_llena_el_arbol_y_activa_las_acciones()
    {
        await ScanAsync();

        Assert.True(Named<CollectionView>(_page, "TreeList").IsVisible);
        Assert.NotEmpty(Vm.Rows);
        Assert.True(Named<Button>(_page, "ScanButton").IsEnabled);
        Assert.False(Named<Button>(_page, "StopButton").IsEnabled);
        Assert.True(Named<Button>(_page, "ExportButton").IsEnabled);
        Assert.False(string.IsNullOrEmpty(Named<Label>(_page, "StatusLabel").Text));
        // La vista activa (arbol) lleva el boton relleno con el icono blanco.
        Assert.Equal(Application.Current!.Resources["Primary"], Named<Button>(_page, "TreeButton").BackgroundColor);
    }

    [Fact]
    public async Task Las_vistas_se_cambian_con_sus_botones()
    {
        await ScanAsync();

        Click(_page, "MapButton");
        await Settle();
        Assert.True(Named<View>(_page, "MapPanel").IsVisible);
        Assert.False(Named<CollectionView>(_page, "TreeList").IsVisible);
        Assert.False(string.IsNullOrEmpty(Named<Label>(_page, "MapLabel").Text));

        Click(_page, "LargestButton");
        await Settle();
        var largest = Named<CollectionView>(_page, "LargestList");
        Assert.True(largest.IsVisible);
        Assert.Same(Vm.Largest, largest.ItemsSource);

        Click(_page, "TypesButton");
        await Settle();
        Assert.True(Named<CollectionView>(_page, "AggregateList").IsVisible);

        Click(_page, "AgeButton");
        await Settle();
        Assert.Same(Vm.Aggregate, Named<CollectionView>(_page, "AggregateList").ItemsSource);

        Click(_page, "DuplicatesButton");
        for (var i = 0; i < 300 && Vm.Duplicates is null; i++)
            await Task.Delay(10);
        await Settle();
        var duplicates = Named<CollectionView>(_page, "DuplicatesList");
        Assert.True(duplicates.IsVisible);
        Assert.Same(Vm.Duplicates, duplicates.ItemsSource);
        Assert.Equal(Colors.Transparent, Named<Button>(_page, "TreeButton").BackgroundColor);
    }

    [Fact]
    public async Task Desplegar_una_carpeta_y_volver_a_plegarla()
    {
        await ScanAsync();
        var fotos = DiskUsageViewModel.FindNode(Vm.Root!, P("Fotos"))!;
        var before = Vm.Rows.Count;
        var expander = new Border { BindingContext = fotos };

        Invoke(_page, "OnExpanderTapped", expander, null);
        Assert.True(Vm.Rows.Count > before);
        Invoke(_page, "OnExpanderTapped", expander, null);
        Assert.Equal(before, Vm.Rows.Count);

        // Pasar el raton marca el desplegable de una carpeta con hijos; al salir se desmarca.
        Invoke(_page, "OnExpanderPointerEntered", expander, null);
        Assert.NotEqual(Colors.Transparent, expander.BackgroundColor);
        Invoke(_page, "OnExpanderPointerExited", expander, null);
        Assert.Equal(Colors.Transparent, expander.BackgroundColor);
        Invoke(_page, "OnExpanderTapped", new Border(), null);   // sin carpeta: no hace nada
    }

    [Fact]
    public async Task Elegir_una_fila_habilita_abrir_y_abrir_la_ensena()
    {
        await ScanAsync();
        var docs = DiskUsageViewModel.FindNode(Vm.Root!, P("Docs"))!;

        Named<CollectionView>(_page, "TreeList").SelectedItem = docs;

        Assert.True(Named<Button>(_page, "OpenButton").IsEnabled);
        Click(_page, "OpenButton");
        await Settle();
        Assert.Equal([P("Docs")], Shell.Revealed);
    }

    [Fact]
    public async Task Marcar_y_mandar_a_la_papelera()
    {
        await ScanAsync();
        var docs = DiskUsageViewModel.FindNode(Vm.Root!, P("Docs"))!;
        docs.IsChecked = true;
        Invoke(_page, "OnItemCheckedChanged", null, new CheckedChangedEventArgs(true));
        Assert.True(Named<Button>(_page, "DeleteButton").IsEnabled);

        Click(_page, "DeleteButton");
        await AnswerDialog(_page, L("DiskChoiceRecycle"));
        for (var i = 0; i < 300 && Shell.Recycled.Count == 0; i++)
            await Task.Delay(10);
        await Settle();

        Assert.Equal([P("Docs")], Shell.Recycled);
        Assert.False(Directory.Exists(P("Docs")));
        Assert.Null(DiskUsageViewModel.FindNode(Vm.Root!, P("Docs")));
    }

    [Fact]
    public async Task Un_fallo_al_pulsar_se_explica_en_un_dialogo()
    {
        await ScanAsync();
        Shell.RevealError = new InvalidOperationException("sin Explorador");
        Named<CollectionView>(_page, "TreeList").SelectedItem = Vm.Root;

        Click(_page, "OpenButton");
        var text = await AnswerDialog(_page, L("Ok"));

        Assert.Contains("sin Explorador", text);
    }

    [Fact]
    public async Task Copiar_sin_portapapeles_se_explica_y_cargar_sin_ruta_no_escanea()
    {
        typeof(VisualElement).GetMethod("SendLoaded", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!.Invoke(_page, null);
        Assert.Null(Vm.Root);                                    // sin ruta pendiente: espera al usuario

        await ScanAsync();
        Named<CollectionView>(_page, "TreeList").SelectedItem = Vm.Root!.Children[0];
        // Sin plataforma no hay portapapeles: el fallo se explica en vez de cerrar la app.
        Click(_page, "CopyButton");
        Assert.False(string.IsNullOrEmpty(await AnswerDialog(_page, L("Ok"))));
    }

    [Fact]
    public async Task Duplicados_tocar_una_fila_la_marca()
    {
        await ScanAsync();
        Click(_page, "DuplicatesButton");
        for (var i = 0; i < 300 && Vm.Duplicates is null; i++)
            await Task.Delay(10);
        var row = Vm.Duplicates!.SelectMany(g => g.Rows).First();

        Invoke(_page, "OnDuplicateRowTapped", new Grid { BindingContext = row }, null);

        Assert.True(row.IsSelected);
        Invoke(_page, "OnDuplicateRowTapped", new Grid(), null);   // sin fila: nada
    }

    [Fact]
    public async Task Mapa_subir_y_toques_sin_posicion()
    {
        await ScanAsync();
        Click(_page, "MapButton");
        await Settle();

        // Sin plataforma el toque no trae posicion: no hace nada (y no revienta).
        Invoke(_page, "OnMapTapped", null, new TappedEventArgs(null));
        Invoke(_page, "OnMapDoubleTapped", null, new TappedEventArgs(null));
        Click(_page, "UpButton");

        Assert.Same(Vm.Root, Vm.MapRoot);
    }

    [Fact]
    public async Task Parar_corta_el_escaneo()
    {
        for (var i = 0; i < 300; i++)
            _tree.File($@"Muchos\{i}\f.bin", 10);
        Named<Entry>(_page, "PathEntry").Text = _tree.Root;

        Click(_page, "ScanButton");
        Click(_page, "StopButton");
        for (var i = 0; i < 300 && Vm.IsScanning; i++)
            await Task.Delay(10);
        await Settle();

        Assert.False(Vm.IsScanning);
        Assert.True(Named<Button>(_page, "ScanButton").IsEnabled);
    }

    [Fact]
    public async Task Ruta_pendiente_de_la_linea_de_ordenes_se_escanea_al_cargar()
    {
        DiskUsagePage.PendingPath = _tree.Root;
        DiskUsagePage.PendingMap = true;
        var page = new DiskUsagePage();

        typeof(VisualElement).GetMethod("SendLoaded", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!.Invoke(page, null);
        var vm = (DiskUsageViewModel)typeof(DiskUsagePage).GetField("_vm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        for (var i = 0; i < 300 && (vm.IsScanning || vm.Root is null); i++)
            await Task.Delay(10);
        await Settle();

        Assert.Null(DiskUsagePage.PendingPath);
        Assert.Equal(DiskViewMode.Map, vm.Mode);
        Assert.Equal(_tree.Root, Named<Entry>(page, "PathEntry").Text);
    }

    [Fact]
    public void Redimensionar_vuelve_a_medir_las_listas_en_dos_pasadas()
    {
        Invoke(_page, "RelayoutLists");
        var timer = Dispatcher.Timers.Last();
        Assert.True(timer.IsRunning);

        Dispatcher.TickAll();
        Assert.True(timer.IsRunning);
        Dispatcher.TickAll();
        Assert.False(timer.IsRunning);

        Invoke(_page, "RelayoutLists");   // reutiliza el mismo temporizador
        Assert.Same(timer, Dispatcher.Timers.Last());
    }

    [Fact]
    public void La_cola_cancela_y_descarta_trabajos()
    {
        Invoke(_page, "OnJobCancelClicked", new Button(), EventArgs.Empty);    // sin trabajo: nada
        Invoke(_page, "OnJobDismissClicked", new Button(), EventArgs.Empty);
        Assert.Empty(Vm.Queue.Jobs);
    }

    [Fact]
    public void Cambiar_de_idioma_repinta()
    {
        Loc.SetLanguage("es");
        Assert.Equal(L("DiskTitle"), _page.Title);
        Assert.Equal(L("DiskPathPlaceholder"), Named<Entry>(_page, "PathEntry").Placeholder);
    }

    [Fact]
    public void Temporizador_y_progreso_de_la_vista()
    {
        IDiskView view = _page;
        var ticks = 0;
        using (view.StartTimer(TimeSpan.FromSeconds(1), () => ticks++))
            Dispatcher.TickAll();
        Dispatcher.TickAll();
        Assert.Equal(1, ticks);

        var got = 0;
        view.CreateProgress<int>(v => got = v);
        var ran = false;
        view.RunOnUi(() => ran = true);
        Assert.True(ran);
        Assert.False(view.IsDarkTheme);
    }
}
