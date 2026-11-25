using Uninstaller.Models;
using Uninstaller.Services;
using Uninstaller.Tests.Fakes;
using Uninstaller.ViewModels;

namespace Uninstaller.Tests;

/// <summary>Duplicados al borrar y al actualizar ramas, y la busqueda parada o con fallo a medias.</summary>
[Collection("Cultura")]
public sealed class DiskUsageDuplicatesTests : ConIdioma
{
    private readonly TempTree _tree = new();
    private readonly FakeShell _shell = new();
    private readonly FakeDiskView _view = new();

    public override void Dispose()
    {
        _tree.Dispose();
        base.Dispose();
    }

    private string P(string relative) => Path.Combine(_tree.Root, relative);

    private async Task<DiskUsageViewModel> ScanAsync(IDiskView? view = null)
    {
        var vm = new DiskUsageViewModel(Loc, _shell, Toast, Dialogs, view ?? _view) { PathText = _tree.Root };
        await vm.ScanAsync();
        Assert.NotNull(vm.Root);
        return vm;
    }

    [Fact]
    public async Task Tres_copias_borrar_una_deja_el_grupo_con_dos_y_los_demas_igual()
    {
        _tree.File(@"Fotos\a.jpg", 70_000, new DateTime(2020, 1, 1));
        _tree.File(@"Fotos\b.jpg", 70_000, new DateTime(2021, 1, 1));
        _tree.File(@"Fotos\c.jpg", 70_000, new DateTime(2022, 1, 1));
        _tree.File(@"Docs\x.pdf", 80_000, DateTime.Now, fill: 3);
        _tree.File(@"Docs\y.pdf", 80_000, DateTime.Now, fill: 3);
        var vm = await ScanAsync();
        await vm.ChooseViewAsync(DiskViewMode.Duplicates);
        Assert.Equal(2, vm.Duplicates!.Count);
        var three = vm.Duplicates.Single(g => g.Files.Count == 3);
        var two = vm.Duplicates.Single(g => g.Files.Count == 2);

        vm.SelectDuplicate(three.Rows.Single(r => r.FullPath == P(@"Fotos\c.jpg")));
        Dialogs.Answer(L("DiskChoiceRecycle"));
        await vm.DeleteAsync();
        await vm.Queue.WhenIdle();

        Assert.Equal(2, vm.Duplicates!.Count);
        Assert.Contains(two, vm.Duplicates);                       // el otro grupo, tal cual
        var pruned = vm.Duplicates.Single(g => g != two);
        Assert.Equal([P(@"Fotos\a.jpg"), P(@"Fotos\b.jpg")], pruned.Files.Select(f => f.FullPath).Order());
        Assert.Equal(2, pruned.Rows.Count);
    }

    [Fact]
    public async Task Actualizar_una_rama_quita_de_duplicados_lo_que_ya_no_esta()
    {
        _tree.File(@"Fotos\a.jpg", 70_000, new DateTime(2020, 1, 1));
        _tree.File(@"Fotos\Viejas\c.jpg", 70_000, new DateTime(2010, 1, 1));
        var vm = await ScanAsync();
        await vm.ChooseViewAsync(DiskViewMode.Duplicates);
        Assert.Single(vm.Duplicates!);

        File.Delete(P(@"Fotos\Viejas\c.jpg"));                    // desaparece por fuera de la app
        vm.RefreshBranch(DiskUsageViewModel.FindNode(vm.Root!, P(@"Fotos\Viejas"))!);
        await vm.Queue.WhenIdle();

        Assert.Empty(vm.Duplicates!);
        Assert.Equal(70_000, vm.Root!.Size);
    }

    [Fact]
    public async Task Borrar_para_siempre_desde_la_papelera_y_cancelar_no_borra()
    {
        var file = _tree.File("ya-en-la-papelera.bin", 500);
        var vm = await ScanAsync();
        _shell.InBin.Add(file);

        Dialogs.Answer(false);
        await vm.DeleteAsync([file]);
        await vm.Queue.WhenIdle();

        Assert.Equal(L("DiskDeleteForever"), Dialogs.Last.Title);
        Assert.True(File.Exists(file));
        Assert.Empty(_shell.DeletedForever);
    }

    [Fact]
    public async Task Muchos_sin_permiso_se_resumen_al_pedirlo()
    {
        var files = Enumerable.Range(1, 8).Select(i => _tree.File($@"Bloqueados\f{i}.bin", 100)).ToList();
        var vm = await ScanAsync();
        foreach (var f in files)
            _shell.Locked.Add(f);

        Dialogs.Answer(L("DiskChoiceRecycle"), false);
        await vm.DeleteAsync(files);
        await vm.Queue.WhenIdle();

        var ask = Dialogs.Calls.Single(c => c.Title == L("DiskPermissionsTitle"));
        Assert.Contains(string.Format(Loc.CurrentCulture, L("DiskAndMore"), 2), ask.Message);
        Assert.Contains(files[5], ask.Message);
        Assert.DoesNotContain(files[6], ask.Message);
        Assert.All(files, f => Assert.True(File.Exists(f)));
    }

    [Fact]
    public void Actualizar_una_rama_sin_nada_escaneado_no_hace_nada()
    {
        var vm = new DiskUsageViewModel(Loc, _shell, Toast, Dialogs, _view);
        vm.RefreshBranch(new FolderNode { Name = "x", FullPath = P("x") });
        Assert.Empty(vm.Queue.Jobs);
    }

    [Fact]
    public async Task Parar_la_busqueda_de_duplicados_a_medias()
    {
        for (var i = 0; i < 60; i++)
            _tree.File($@"Copias\f{i:00}.bin", 66_000, DateTime.Now, fill: (byte)(i % 2));
        var view = new ProgressView(_view, stop: true);
        var vm = await ScanAsync(view);
        view.Vm = vm;

        await vm.ChooseViewAsync(DiskViewMode.Duplicates);

        Assert.True(view.Reports > 0);
        Assert.Equal(L("DiskCancelled"), vm.StatusText);
        Assert.Null(vm.Duplicates);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task Un_fallo_en_la_busqueda_de_duplicados_se_explica()
    {
        for (var i = 0; i < 60; i++)
            _tree.File($@"Copias\f{i:00}.bin", 66_000, DateTime.Now, fill: (byte)(i % 2));
        var view = new ProgressView(_view, stop: false);
        var vm = await ScanAsync(view);
        view.Vm = vm;
        view.Fail = true;

        await vm.ChooseViewAsync(DiskViewMode.Duplicates);

        Assert.Equal(L("DiskHint"), vm.StatusText);
        Assert.Equal(L("Error"), Dialogs.Last.Title);
        Assert.False(vm.IsScanning);
    }

    /// <summary>Vista cuyo progreso de duplicados para la busqueda (o falla) al primer aviso.</summary>
    private sealed class ProgressView(FakeDiskView inner, bool stop) : IDiskView
    {
        public DiskUsageViewModel? Vm { get; set; }
        public bool Fail { get; set; }
        public int Reports;

        public void RunOnUi(Action action) => action();
        public IProgress<T> CreateProgress<T>(Action<T> handler) => inner.CreateProgress<T>(value =>
        {
            handler(value);
            if (Vm is null)   // el escaneo de antes: nada
                return;
            Reports++;
            if (Fail)
                throw new InvalidOperationException("disco desconectado");
            if (stop)
                Vm.Stop();
        });
        public IDisposable StartTimer(TimeSpan interval, Action tick) => inner.StartTimer(interval, tick);
        public bool IsDarkTheme => false;
        public Task SetClipboardTextAsync(string text) => inner.SetClipboardTextAsync(text);
    }
}
