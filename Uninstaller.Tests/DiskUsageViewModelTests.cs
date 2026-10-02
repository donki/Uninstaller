using System.Text;
using Uninstaller.Models;
using Uninstaller.Services;
using Uninstaller.Tests.Fakes;
using Uninstaller.ViewModels;

namespace Uninstaller.Tests;

/// <summary>
/// Logica del espacio en disco sobre un arbol de carpetas temporal de verdad; el Explorador, la
/// papelera y los permisos los hace un doble que borra de verdad dentro de esa carpeta.
/// </summary>
[Collection("Cultura")]
public sealed class DiskUsageViewModelTests : ConIdioma
{
    private readonly TempTree _tree = new();
    private readonly FakeShell _shell = new();
    private readonly FakeDiskView _view = new();
    private readonly DiskUsageViewModel _vm;
    private int _changes;

    public DiskUsageViewModelTests()
    {
        _vm = new DiskUsageViewModel(Loc, _shell, Toast, Dialogs, _view);
        _vm.Changed += (_, _) => _changes++;
        _tree.File(@"Fotos\a.jpg", 70_000, new DateTime(2020, 1, 1));
        _tree.File(@"Fotos\b.jpg", 70_000, new DateTime(2026, 9, 1), fill: 2);
        _tree.File(@"Fotos\Viejas\c.jpg", 70_000, new DateTime(2010, 1, 1));   // igual que a.jpg
        _tree.File(@"Docs\informe.pdf", 1000, DateTime.Now);
        _tree.File(@"Docs\notas.txt", 10, DateTime.Now);
        _tree.File(@"suelto.bin", 500, DateTime.Now);
    }

    public override void Dispose()
    {
        _tree.Dispose();
        base.Dispose();
    }

    private string P(string relative) => Path.Combine(_tree.Root, relative);

    private async Task ScanAsync(bool map = false)
    {
        _vm.PathText = "  \"" + _tree.Root + "\" ";
        await _vm.ScanAsync(map);
        Assert.NotNull(_vm.Root);
    }

    private FolderNode Node(string relative) => DiskUsageViewModel.FindNode(_vm.Root!, P(relative))!;

    // ---------- Unidades ----------

    [Fact]
    public void Unidades_con_espacio_usado_y_la_primera_elegida()
    {
        _shell.Drives.Add(new DriveEntry(@"C:\", "Windows (C:)", 1000L << 30, 400L << 30, false));
        _shell.Drives.Add(new DriveEntry(@"\\nas\share", "nas", 0, 0, true));

        _vm.LoadDrives();

        Assert.Equal($"Windows (C:) · {_vm.FormatSize(600L << 30)} / {_vm.FormatSize(1000L << 30)}", _vm.DriveLabels[0]);
        Assert.Equal("nas", _vm.DriveLabels[1]);
        Assert.Equal(0, _vm.SelectedDriveIndex);
        Assert.Equal(@"C:\", _vm.PathText);

        _vm.SelectDrive(1);
        Assert.Equal(@"\\nas\share", _vm.PathText);
        _vm.SelectDrive(7);
        Assert.Equal(@"\\nas\share", _vm.PathText);
    }

    [Fact]
    public void Sin_unidades_no_se_elige_ninguna()
    {
        _vm.LoadDrives();
        Assert.Empty(_vm.DriveLabels);
        Assert.Equal(-1, _vm.SelectedDriveIndex);
        Assert.Equal(L("DiskHint"), _vm.StatusText);
    }

    // ---------- Escaneo ----------

    [Fact]
    public async Task Escanear_deja_el_arbol_los_textos_y_el_resumen()
    {
        await ScanAsync();

        var root = _vm.Root!;
        Assert.Equal(211510, root.Size);
        Assert.Equal(6, root.FileCount);
        Assert.Equal(new[] { root.FullPath, P("Fotos"), P("Docs") }, _vm.Rows.Select(r => r.FullPath));
        Assert.Equal(_vm.FormatSize(210000), Node("Fotos").SizeText);
        Assert.StartsWith(string.Format(Loc.CurrentCulture, L("DiskFolderDetail"), (210000 * 100.0 / 211510).ToString("0.#", Loc.CurrentCulture), 3, 1, "").TrimEnd(), Node("Fotos").DetailText);
        Assert.StartsWith($"{root.FullPath}: {_vm.FormatSize(211510)}", _vm.StatusText);
        Assert.True(_vm.ScanEnabled);
        Assert.False(_vm.StopEnabled);
        Assert.False(_vm.ProgressVisible);
        Assert.False(_vm.IsScanning);
        Assert.True(_vm.ExportEnabled);
        Assert.Equal(1, _view.TimersStarted);
        Assert.Equal(1, _view.TimersStopped);
    }

    [Fact]
    public async Task Ruta_vacia_no_escanea_y_ruta_que_no_existe_se_explica()
    {
        _vm.PathText = "   ";
        await _vm.ScanAsync();
        Assert.Null(_vm.Root);
        Assert.Empty(Dialogs.Calls);

        _vm.PathText = P("no-existe");
        await _vm.ScanAsync();
        Assert.Null(_vm.Root);
        Assert.Empty(_vm.Rows);
        Assert.Equal(L("DiskHint"), _vm.StatusText);
        Assert.StartsWith(string.Format(L("DiskScanError"), P("no-existe"), "").TrimEnd(), Dialogs.Last.Message);
    }

    [Fact]
    public async Task Parar_el_escaneo_deja_lo_que_hubiera()
    {
        var gate = new TaskCompletionSource();
        _vm.PathText = _tree.Root;
        var view = new BlockingView(_view, gate, _vm);
        var vm = new DiskUsageViewModel(Loc, _shell, Toast, Dialogs, view);
        view.Vm = vm;
        vm.PathText = _tree.Root;

        await vm.ScanAsync();

        Assert.Equal(L("DiskCancelled"), vm.StatusText);
        Assert.NotNull(vm.Root);
        Assert.False(vm.IsScanning);
    }

    /// <summary>Vista que pide parar el escaneo en cuanto llega la raiz.</summary>
    private sealed class BlockingView(FakeDiskView inner, TaskCompletionSource gate, DiskUsageViewModel first) : IDiskView
    {
        public DiskUsageViewModel Vm { get; set; } = first;
        public void RunOnUi(Action action) { action(); Vm.Stop(); }
        public IProgress<T> CreateProgress<T>(Action<T> handler) => inner.CreateProgress(handler);
        public IDisposable StartTimer(TimeSpan interval, Action tick) => inner.StartTimer(interval, tick);
        public bool IsDarkTheme => false;
        public Task SetClipboardTextAsync(string text) => inner.SetClipboardTextAsync(text);
    }

    [Fact]
    public async Task Un_segundo_escaneo_empieza_de_cero()
    {
        await ScanAsync();
        await _vm.ChooseViewAsync(DiskViewMode.Largest);
        Assert.NotNull(_vm.Largest);

        await ScanAsync();

        // Como antes: al escanear desde una lista se vuelve al arbol.
        Assert.Null(_vm.Largest);
        Assert.Equal(DiskViewMode.Tree, _vm.Mode);
    }

    [Fact]
    public async Task Nombres_del_sistema_y_papelera()
    {
        _shell.Names[P("Docs")] = "Documentos";
        _shell.BinFolders.Add(P("Fotos"));
        _shell.Owners[P("Fotos")] = "Josep";

        await ScanAsync();

        Assert.Equal("Documentos", Node("Docs").Display);
        Assert.Equal($"{L("DiskRecycleBin")} · Josep", Node("Fotos").Display);
    }

    [Fact]
    public async Task Papelera_sin_dueno_y_carpeta_inaccesible()
    {
        _shell.BinFolders.Add(P("Fotos"));
        await ScanAsync();
        Assert.Equal(L("DiskRecycleBin"), Node("Fotos").Display);

        var docs = Node("Docs");
        docs.Inaccessible = true;
        docs.LastModified = DateTime.MinValue;
        _vm.DescribeOne(docs);
        Assert.EndsWith($"— · {L("DiskInaccessible")}", docs.DetailText);
    }

    // ---------- Arbol ----------

    [Fact]
    public async Task Desplegar_y_plegar_una_carpeta()
    {
        await ScanAsync();
        var fotos = Node("Fotos");

        _vm.Toggle(fotos);
        Assert.Equal(4, _vm.Rows.Count);
        Assert.Equal(P(@"Fotos\Viejas"), _vm.Rows[2].FullPath);

        _vm.Toggle(fotos);
        Assert.Equal(3, _vm.Rows.Count);

        _vm.Toggle(Node("Docs"));                 // sin subcarpetas: nada
        _vm.Toggle(Node(@"Fotos\Viejas"));        // no esta a la vista: nada
        Assert.Equal(3, _vm.Rows.Count);
    }

    [Fact]
    public async Task RefreshLive_solo_toca_la_lista_si_cambio()
    {
        await ScanAsync();
        var before = _vm.Rows.ToList();
        _vm.RefreshLive();
        Assert.Equal(before, _vm.Rows);
    }

    // ---------- Vistas ----------

    [Fact]
    public async Task Los_mayores_por_tamano()
    {
        _shell.Names[P(@"Docs\informe.pdf")] = "Informe";
        await ScanAsync();

        await _vm.ChooseViewAsync(DiskViewMode.Largest);

        Assert.Equal(DiskViewMode.Largest, _vm.Mode);
        Assert.Equal(6, _vm.Largest!.Count);
        Assert.Equal(70_000, _vm.Largest[0].File.Size);
        Assert.Equal("Informe", _vm.Largest.Single(r => r.Name == "informe.pdf").Display);
        Assert.Equal("notas.txt", _vm.Largest.Single(r => r.Name == "notas.txt").Display);
        var same = _vm.Largest;
        _vm.ShowView(DiskViewMode.Largest);
        Assert.Same(same, _vm.Largest);
    }

    [Fact]
    public async Task Reparto_por_tipo_y_por_antiguedad()
    {
        await ScanAsync();

        await _vm.ChooseViewAsync(DiskViewMode.Types);
        var jpg = _vm.Aggregate!.First();
        Assert.Equal(210000, jpg.Size);
        Assert.Equal(3, jpg.Count);

        await _vm.ChooseViewAsync(DiskViewMode.Age);
        Assert.Equal(5, _vm.Aggregate!.Count);
        Assert.Equal(L("DiskAge1"), _vm.Aggregate[0].Label);
        Assert.Equal(6, _vm.Aggregate.Sum(a => a.Count));
        Assert.Equal(211510, _vm.Aggregate.Sum(a => a.Size));
    }

    [Fact]
    public async Task Duplicados_se_buscan_la_primera_vez()
    {
        await ScanAsync();

        await _vm.ChooseViewAsync(DiskViewMode.Duplicates);

        var group = Assert.Single(_vm.Duplicates!);
        Assert.Equal(2, group.Files.Count);
        Assert.Equal(2, group.Rows.Count);
        Assert.Equal(string.Format(Loc.CurrentCulture, L("DiskDuplicatesDone"), 1, _vm.FormatSize(70_000)), _vm.StatusText);
        Assert.False(_vm.IsScanning);

        var same = _vm.Duplicates;
        await _vm.ChooseViewAsync(DiskViewMode.Duplicates);
        Assert.Same(same, _vm.Duplicates);

        // Un escaneo nuevo estando en duplicados vuelve al arbol.
        await ScanAsync();
        Assert.Equal(DiskViewMode.Tree, _vm.Mode);
    }

    [Fact]
    public async Task Vistas_sin_nada_escaneado_no_calculan()
    {
        await _vm.ChooseViewAsync(DiskViewMode.Duplicates);
        await _vm.ChooseViewAsync(DiskViewMode.Types);
        Assert.Null(_vm.Duplicates);
        Assert.Null(_vm.Aggregate);
        Assert.False(_vm.ExportEnabled);
    }

    // ---------- Mapa ----------

    [Fact]
    public async Task Mapa_tocar_entrar_y_subir()
    {
        await ScanAsync(map: true);
        Assert.Equal(DiskViewMode.Map, _vm.Mode);
        Assert.Same(_vm.Root, _vm.MapRoot);
        Assert.Equal($"{_vm.Root!.FullPath} · {_vm.FormatSize(211510)}", _vm.MapLabel);
        Assert.False(_vm.UpEnabled);

        _vm.Map.Draw(new TreemapTests.RecordingCanvas(), new RectF(0, 0, 800, 500));
        var fotosTile = _vm.Map.Tiles.First(t => t.Node.FullPath == P("Fotos"));
        var center = new PointF(fotosTile.Bounds.Center.X, fotosTile.Bounds.Center.Y);

        _vm.MapTapped(center);
        Assert.Equal(P("Fotos"), _vm.MapSelected!.FullPath);
        Assert.Contains(" %", _vm.MapLabel);
        Assert.Equal(P("Fotos"), _vm.SelectedPath());
        var version = _vm.MapVersion;

        _vm.MapDoubleTapped(center);
        Assert.Equal(P("Fotos"), _vm.MapRoot!.FullPath);
        Assert.Null(_vm.MapSelected);
        Assert.True(_vm.UpEnabled);
        Assert.True(_vm.MapVersion > version);

        _vm.MapUp();
        Assert.Same(_vm.Root, _vm.MapRoot);
        Assert.Equal(P("Fotos"), _vm.MapSelected!.FullPath);
        _vm.MapUp();
        Assert.Same(_vm.Root, _vm.MapRoot);
    }

    [Fact]
    public async Task Mapa_doble_toque_fuera_o_en_carpeta_sin_hijas_no_entra()
    {
        await ScanAsync(map: true);
        _vm.Map.Draw(new TreemapTests.RecordingCanvas(), new RectF(0, 0, 800, 500));
        var docs = _vm.Map.Tiles.First(t => t.Node.FullPath == P("Docs"));

        _vm.MapDoubleTapped(new PointF(-50, -50));
        _vm.MapDoubleTapped(new PointF(docs.Bounds.Center.X, docs.Bounds.Center.Y));

        Assert.Same(_vm.Root, _vm.MapRoot);
    }

    // ---------- Seleccion y acciones ----------

    [Fact]
    public async Task Botones_segun_lo_elegido_y_la_raiz_no_se_borra()
    {
        await ScanAsync();
        Assert.False(_vm.OpenEnabled);
        Assert.False(_vm.DeleteEnabled);

        _vm.TreeSelected = _vm.Root;
        _vm.UpdateActions();
        Assert.True(_vm.OpenEnabled);
        Assert.False(_vm.DeleteEnabled);

        _vm.TreeSelected = Node("Docs");
        _vm.UpdateActions();
        Assert.True(_vm.DeleteEnabled);
        Assert.True(_vm.CopyEnabled);
        Assert.Equal(L("DiskDelete"), _vm.DeleteTooltip);

        Node("Docs").IsChecked = true;
        Node("Fotos").IsChecked = true;
        _vm.UpdateActions();
        Assert.Equal(string.Format(L("DiskDeleteMany"), 2), _vm.DeleteTooltip);
    }

    [Fact]
    public async Task Marcadas_dentro_de_otra_marcada_cuentan_una_vez()
    {
        await ScanAsync();
        _vm.Root!.IsChecked = true;
        Node("Fotos").IsChecked = true;
        Node(@"Fotos\Viejas").IsChecked = true;

        Assert.Equal(new[] { P("Fotos") }, _vm.CheckedPaths());
        Assert.Equal(new[] { P("Fotos") }, _vm.TargetPaths());
    }

    [Fact]
    public async Task Marcadas_en_los_mayores_y_en_duplicados()
    {
        await ScanAsync();
        await _vm.ChooseViewAsync(DiskViewMode.Largest);
        _vm.Largest!.Single(r => r.Name == "suelto.bin").IsChecked = true;
        Assert.Equal(new[] { P("suelto.bin") }, _vm.CheckedPaths());

        _vm.LargestSelected = _vm.Largest.Single(r => r.Name == "notas.txt");
        Assert.Equal(P(@"Docs\notas.txt"), _vm.SelectedPath());

        await _vm.ChooseViewAsync(DiskViewMode.Duplicates);
        Assert.Empty(_vm.CheckedPaths());
        var rows = _vm.Duplicates![0].Rows;
        rows[0].IsChecked = true;
        Assert.Single(_vm.CheckedPaths());

        _vm.SelectDuplicate(rows[0]);
        _vm.SelectDuplicate(rows[1]);
        Assert.False(rows[0].IsSelected);
        Assert.True(rows[1].IsSelected);
        Assert.Equal(rows[1].FullPath, _vm.SelectedPath());

        await _vm.ChooseViewAsync(DiskViewMode.Types);
        Assert.Empty(_vm.CheckedPaths());
        Assert.Null(_vm.SelectedPath());
    }

    [Fact]
    public async Task Abrir_en_el_Explorador_o_explicar_el_error()
    {
        await ScanAsync();
        await _vm.OpenAsync();
        Assert.Empty(_shell.Revealed);

        _vm.TreeSelected = Node("Docs");
        await _vm.OpenAsync();
        Assert.Equal(new[] { P("Docs") }, _shell.Revealed);

        _shell.RevealError = new IOException("no hay Explorador");
        await _vm.OpenAsync();
        Assert.Equal("no hay Explorador", Dialogs.Last.Message);
    }

    [Fact]
    public async Task Copiar_rutas_una_o_varias()
    {
        await ScanAsync();
        await _vm.CopyAsync();
        Assert.Null(_view.Clipboard);

        _vm.TreeSelected = Node("Docs");
        await _vm.CopyAsync();
        Assert.Equal(P("Docs"), _view.Clipboard);
        Assert.Equal(L("DiskCopied"), Toast.Shown[^1]);

        Node("Docs").IsChecked = true;
        Node("Fotos").IsChecked = true;
        await _vm.CopyAsync();
        Assert.Equal(2, _view.Clipboard!.Split(Environment.NewLine).Length);
        Assert.Equal(string.Format(L("DiskCopiedMany"), 2), Toast.Shown[^1]);
    }

    // ---------- Borrar ----------

    [Fact]
    public async Task Borrar_una_carpeta_a_la_papelera_resta_los_tamanos()
    {
        await ScanAsync();
        _vm.TreeSelected = Node("Docs");

        Dialogs.Answer(true);
        await _vm.DeleteAsync();

        Assert.Equal(string.Format(L("DiskDeleteFolderConfirm"), P("Docs")), Dialogs.Last.Message);
        Assert.False(Directory.Exists(P("Docs")));
        Assert.Equal(210500, _vm.Root!.Size);
        Assert.Equal(4, _vm.Root.FileCount);
        Assert.Equal(2, _vm.Rows.Count);
        Assert.Equal(new[] { L("DiskDeleted") }, Toast.Shown);
        Assert.False(_vm.BusyVisible);
    }

    [Fact]
    public async Task Borrar_cancelado_no_borra()
    {
        await ScanAsync();
        _vm.TreeSelected = Node("Docs");

        Dialogs.Answer(false);
        await _vm.DeleteAsync();

        Assert.True(Directory.Exists(P("Docs")));
        Assert.Empty(Toast.Shown);
    }

    [Fact]
    public async Task Borrar_sin_nada_elegido_no_pregunta()
    {
        await ScanAsync();
        await _vm.DeleteAsync();
        Assert.Empty(Dialogs.Calls);
    }

    [Fact]
    public async Task Carpeta_de_riesgo_avisa_primero_y_Cancelar_es_lo_principal()
    {
        await ScanAsync();
        _shell.Risks[P("Docs")] = "DiskRiskSystem";
        _vm.TreeSelected = Node("Docs");

        Dialogs.Answer(true);   // «Cancelar» es el boton principal
        await _vm.DeleteAsync();

        Assert.Equal(L("DiskRiskTitle"), Dialogs.Last.Title);
        Assert.Equal(L("Cancel"), Dialogs.Last.Options[0]);
        Assert.True(Directory.Exists(P("Docs")));

        Dialogs.Answer(false, true);
        await _vm.DeleteAsync();
        Assert.False(Directory.Exists(P("Docs")));
    }

    [Fact]
    public async Task Muchos_de_riesgo_se_resumen()
    {
        await ScanAsync();
        await _vm.ChooseViewAsync(DiskViewMode.Largest);
        foreach (var row in _vm.Largest!)
        {
            row.IsChecked = true;
            _shell.Risks[row.FullPath] = "DiskRiskSystem";
        }
        _shell.Risks[P("suelto.bin")] = "DiskRiskSystem";
        _vm.Largest.Add(new FileRow(new ScannedFile(P("extra.bin"), 1, DateTime.Now), "1 B", "", 0) { IsChecked = true });
        _shell.Risks[P("extra.bin")] = "DiskRiskSystem";

        await _vm.DeleteAsync();   // sin respuesta: el doble acepta, que aqui es «Cancelar»

        Assert.Contains(string.Format(L("DiskAndMore"), 1), Dialogs.Last.Message);
    }

    [Fact]
    public async Task Borrar_varios_ficheros_desde_los_mayores()
    {
        await ScanAsync();
        await _vm.ChooseViewAsync(DiskViewMode.Largest);
        foreach (var row in _vm.Largest!.Where(r => r.Name.EndsWith(".jpg")))
            row.IsChecked = true;

        Dialogs.Answer(true);
        await _vm.DeleteAsync();

        Assert.Contains(_vm.FormatSize(210000), Dialogs.Last.Message);
        Assert.Equal(1510, _vm.Root!.Size);
        Assert.Equal(new[] { string.Format(L("DiskDeletedMany"), 3) }, Toast.Shown);
        Assert.Null(Node("Fotos").Files.FirstOrDefault());
        Assert.Equal(DiskViewMode.Largest, _vm.Mode);
        Assert.Equal(3, _vm.Largest!.Count);
    }

    [Fact]
    public void Aviso_de_muchos_lista_los_ocho_primeros_y_cuenta_el_resto()
    {
        var paths = Enumerable.Range(1, 10).Select(i => P($"f{i}.txt")).ToList();
        var message = _vm.DeleteMessage(paths, inBinCount: 0);
        Assert.Contains(string.Format(L("DiskAndMore"), 2), message);
        Assert.DoesNotContain(P("f9.txt"), message);

        var mixed = _vm.DeleteMessage(paths, inBinCount: 3);
        Assert.EndsWith(string.Format(L("DiskDeleteSomeForever"), 3), mixed);

        var forever = _vm.DeleteMessage(paths, inBinCount: 10);
        Assert.StartsWith(string.Format(L("DiskDeleteForeverManyConfirm"), 10, _vm.FormatSize(0), "").Split(Environment.NewLine)[0], forever);
    }

    [Fact]
    public async Task Lo_que_ya_esta_en_la_papelera_se_borra_para_siempre()
    {
        await ScanAsync();
        _shell.InBin.Add(P("suelto.bin"));
        _vm.TreeSelected = null;
        await _vm.ChooseViewAsync(DiskViewMode.Largest);
        _vm.LargestSelected = _vm.Largest!.Single(r => r.Name == "suelto.bin");

        Dialogs.Answer(true);
        await _vm.DeleteAsync();

        Assert.Equal(L("DiskDeleteForever"), Dialogs.Last.Title);
        Assert.Equal(string.Format(L("DiskDeleteForeverFileConfirm"), P("suelto.bin")), Dialogs.Last.Message);
        Assert.Equal(new[] { L("DiskDeletedForever") }, Toast.Shown);
    }

    [Fact]
    public async Task Sin_permiso_se_ofrece_arreglarlo_y_se_reintenta()
    {
        await ScanAsync();
        _shell.Locked.Add(P("Docs"));
        _vm.TreeSelected = Node("Docs");

        Dialogs.Answer(true, true);
        await _vm.DeleteAsync();

        Assert.Equal(L("DiskPermissionsTitle"), Dialogs.Last.Title);
        Assert.Single(_shell.Fixed);
        Assert.False(Directory.Exists(P("Docs")));
        Assert.Equal(new[] { L("DiskDeleted") }, Toast.Shown);
    }

    [Fact]
    public async Task Si_no_se_arregla_el_permiso_se_dice_que_fallo()
    {
        await ScanAsync();
        _shell.Locked.Add(P("Docs"));
        _shell.FixWorks = false;
        _vm.TreeSelected = Node("Docs");

        Dialogs.Answer(true, true);
        await _vm.DeleteAsync();

        Assert.Equal(new[] { L("DiskPermissionsDenied") }, Toast.Shown);
        Assert.Equal(string.Format(L("DiskDeleteFailed"), P("Docs")), Dialogs.Last.Message);
        Assert.True(Directory.Exists(P("Docs")));
    }

    [Fact]
    public async Task Varios_con_fallos_los_lista()
    {
        await ScanAsync();
        _shell.Locked.Add(P("Docs"));
        Node("Docs").IsChecked = true;
        Node("Fotos").IsChecked = true;

        Dialogs.Answer(true, false);   // confirma; no quiere arreglar permisos
        await _vm.DeleteAsync();

        Assert.Equal(string.Format(L("DiskDeleteFailedMany"), 1, P("Docs")), Dialogs.Last.Message);
        Assert.Equal(new[] { L("DiskDeleted") }, Toast.Shown);
    }

    [Fact]
    public async Task Carpeta_inaccesible_pide_permiso_antes_y_si_no_se_da_no_borra()
    {
        await ScanAsync();
        Node("Docs").Inaccessible = true;
        _vm.TreeSelected = Node("Docs");

        Dialogs.Answer(true, false);
        await _vm.DeleteAsync();

        Assert.True(Directory.Exists(P("Docs")));
        Assert.Empty(_shell.Recycled);
    }

    [Fact]
    public async Task Etiqueta_con_el_nombre_del_sistema_y_tamano_de_rutas()
    {
        await ScanAsync();
        _shell.Names[P("Docs")] = "Documentos";
        _shell.Names[P("Fotos")] = "Fotos";

        Assert.Equal($"Documentos — {P("Docs")}", _vm.Label(P("Docs")));
        Assert.Equal(P("Fotos"), _vm.Label(P("Fotos")));
        _shell.BinFolders.Add(P("Docs"));
        Assert.Equal(P("Docs"), _vm.Label(P("Docs")));

        Assert.Equal(1010, _vm.SizeOf(P("Docs")));
        Assert.Equal(500, _vm.SizeOf(P("suelto.bin")));
        Assert.Equal(0, _vm.SizeOf(P(@"Docs\nada.txt")));
        Assert.Null(DiskUsageViewModel.FindNode(_vm.Root!, @"Z:\otra"));
    }

    [Fact]
    public async Task Quitar_del_modelo_algo_que_no_esta_no_hace_nada()
    {
        _vm.RemoveFromModel(P("Docs"));   // sin escanear
        await ScanAsync();
        _vm.RemoveFromModel(P(@"Docs\nada.txt"));
        Assert.Equal(211510, _vm.Root!.Size);

        _vm.RemoveFromModel(P(@"Docs\notas.txt"));
        Assert.Equal(211500, _vm.Root.Size);
        Assert.Equal(1000, Node("Docs").Size);
    }

    [Fact]
    public async Task Borrar_lo_que_mira_el_mapa_lo_devuelve_a_la_raiz()
    {
        await ScanAsync(map: true);
        _vm.Map.Draw(new TreemapTests.RecordingCanvas(), new RectF(0, 0, 800, 500));
        var tile = _vm.Map.Tiles.First(t => t.Node.FullPath == P("Fotos"));
        _vm.MapDoubleTapped(new PointF(tile.Bounds.Center.X, tile.Bounds.Center.Y));
        Assert.True(_vm.DeleteEnabled);

        Dialogs.Answer(true);
        await _vm.DeleteAsync();

        Assert.Same(_vm.Root, _vm.MapRoot);
        Assert.False(Directory.Exists(P("Fotos")));
    }

    // ---------- Exportar ----------

    [Fact]
    public async Task CSV_de_cada_vista()
    {
        await ScanAsync();
        var tree = _vm.BuildCsv();
        Assert.StartsWith("Carpeta;Bytes;Ficheros;Carpetas;Modificado", tree);
        Assert.Equal(5, tree.Trim().Split(Environment.NewLine).Length);

        await _vm.ChooseViewAsync(DiskViewMode.Largest);
        Assert.StartsWith("Ruta;Bytes;Modificado", _vm.BuildCsv());

        await _vm.ChooseViewAsync(DiskViewMode.Types);
        Assert.Contains(".jpg;210000;3", _vm.BuildCsv());

        await _vm.ChooseViewAsync(DiskViewMode.Duplicates);
        var dup = _vm.BuildCsv().Trim().Split(Environment.NewLine);
        Assert.Equal(3, dup.Length);
        Assert.StartsWith("1;", dup[1]);
    }

    [Theory]
    [InlineData("normal", "normal")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("di \"hola\"", "\"di \"\"hola\"\"\"")]
    public void Csv_escapa_punto_y_coma_y_comillas(string input, string expected) =>
        Assert.Equal(expected, DiskUsageViewModel.Csv(input));

    [Fact]
    public async Task Exportar_guarda_con_BOM_y_lo_ensena()
    {
        await ScanAsync();
        using var outDir = new TempTree();

        await _vm.ExportAsync(outDir.Root, new DateTime(2026, 10, 1, 12, 30, 5));

        var file = Path.Combine(outDir.Root, "sOCUninstaller-espacio-20261001-123005.csv");
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(file)[..3]);
        Assert.Equal(new[] { file }, _shell.Revealed);
        Assert.Equal(string.Format(L("DiskExported"), file), Toast.Shown[^1]);
        Assert.False(_vm.BusyVisible);
    }

    [Fact]
    public async Task Exportar_sin_escaneo_no_hace_nada_y_un_error_se_explica()
    {
        await _vm.ExportAsync(_tree.Root, DateTime.Now);
        Assert.Empty(_shell.Revealed);

        await ScanAsync();
        await _vm.ExportAsync(P("no-existe"), DateTime.Now);
        Assert.Equal(L("Error"), Dialogs.Last.Title);
        Assert.False(_vm.BusyVisible);
    }

    [Fact]
    public async Task Cambio_de_idioma_sin_escaneo_traduce_la_pista()
    {
        Loc.SetLanguage("es");
        _vm.LanguageChanged();
        Assert.Equal(L("DiskHint"), _vm.StatusText);

        await ScanAsync();
        var status = _vm.StatusText;
        _vm.LanguageChanged();
        Assert.Equal(status, _vm.StatusText);
        Assert.True(_changes > 0);
    }

    [Fact]
    public void Botones_de_vista_cubren_todas_las_vistas() =>
        Assert.Equal(Enum.GetValues<DiskViewMode>().OrderBy(m => m), DiskUsageViewModel.ViewButtons.Select(v => v.Mode).OrderBy(m => m));
}
