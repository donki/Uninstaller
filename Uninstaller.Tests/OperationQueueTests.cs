using Uninstaller.Services;

namespace Uninstaller.Tests;

/// <summary>La cola de ordenes en segundo plano y el borrado definitivo fichero a fichero.</summary>
public sealed class OperationQueueTests : IDisposable
{
    private readonly OperationQueue _queue = new(a => a());
    private readonly TempTree _tree = new();

    public void Dispose() => _tree.Dispose();

    [Fact]
    public async Task Las_ordenes_van_una_detras_de_otra_y_las_buenas_desaparecen()
    {
        var order = new List<string>();
        var gate = new TaskCompletionSource();
        _queue.Enqueue("a", async (_, _) => { await gate.Task; lock (order) order.Add("a"); }, _ => { order.Add("a!"); return Task.CompletedTask; });
        _queue.Enqueue("b", (_, _) => { lock (order) order.Add("b"); return Task.CompletedTask; });

        Assert.True(_queue.IsBusy);
        Assert.Equal(2, _queue.Jobs.Count);
        Assert.True(_queue.Jobs[1].IsPending);
        gate.SetResult();
        await _queue.WhenIdle();

        Assert.Equal(new[] { "a", "a!", "b" }, order);
        Assert.Empty(_queue.Jobs);
        Assert.False(_queue.IsBusy);
    }

    [Fact]
    public async Task La_que_falla_se_queda_con_el_motivo_hasta_quitarla()
    {
        Exception? seen = null;
        var job = _queue.Enqueue("x", (_, _) => throw new IOException("disco lleno"), j => { seen = j.Error; return Task.CompletedTask; });
        await _queue.WhenIdle();

        Assert.IsType<IOException>(seen);
        Assert.True(job.IsFailed);
        Assert.False(job.CanCancel);
        Assert.Equal("disco lleno", job.Detail);
        Assert.Single(_queue.Jobs);

        _queue.Dismiss(job);
        Assert.Empty(_queue.Jobs);
    }

    [Fact]
    public async Task Parar_una_pendiente_no_la_empieza_y_su_remate_lo_sabe()
    {
        var gate = new TaskCompletionSource();
        var ran = false;
        bool? cancelled = null;
        _queue.Enqueue("larga", async (_, _) => await gate.Task);
        var second = _queue.Enqueue("segunda", (_, _) => { ran = true; return Task.CompletedTask; }, j => { cancelled = j.CancelRequested; return Task.CompletedTask; });

        _queue.Cancel(second);
        gate.SetResult();
        await _queue.WhenIdle();

        Assert.False(ran);
        Assert.True(cancelled);
        Assert.Empty(_queue.Jobs);
    }

    [Fact]
    public async Task Parar_la_que_esta_en_marcha_le_llega_por_el_token()
    {
        var started = new TaskCompletionSource();
        var job = _queue.Enqueue("larga", async (_, token) => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); });
        await started.Task;
        Assert.True(job.IsRunning);

        _queue.Cancel(job);
        await _queue.WhenIdle();

        Assert.Empty(_queue.Jobs);   // parada no es fallo
    }

    [Fact]
    public async Task El_progreso_llega_a_la_orden_y_lo_encolado_en_un_remate_tambien_se_espera()
    {
        var second = false;
        _queue.Enqueue("a", (job, _) => { job.Report("mitad", 0.5); job.Report("de mas", 7); return Task.CompletedTask; },
            j =>
            {
                Assert.Equal("de mas", j.Detail);
                Assert.Equal(1, j.Progress);
                _queue.Enqueue("b", (_, _) => { second = true; return Task.CompletedTask; });
                return Task.CompletedTask;
            });
        await _queue.WhenIdle();
        Assert.True(second);
    }

    [Fact]
    public async Task Un_fallo_en_el_remate_tambien_deja_la_orden_fallida()
    {
        var job = _queue.Enqueue("a", (_, _) => Task.CompletedTask, _ => throw new InvalidOperationException("roto"));
        await _queue.WhenIdle();
        Assert.True(job.IsFailed);
        _queue.Dismiss(job);
        _queue.Dismiss(job);   // dos veces no pasa nada
        Assert.Empty(_queue.Jobs);
    }

    // ---------- Borrado definitivo ----------

    [Fact]
    public void Borra_carpetas_hondas_ficheros_sueltos_y_de_solo_lectura()
    {
        _tree.File(@"A\B\C\D\uno.txt", 10, DateTime.Now);
        _tree.File(@"A\dos.txt", 10, DateTime.Now);
        _tree.File(@"suelto.txt", 10, DateTime.Now);
        var readOnly = Path.Combine(_tree.Root, @"A\B\ro.txt");
        File.WriteAllText(readOnly, "x");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        var reports = new List<DeleteProgress>();

        FileDeleter.Delete([Path.Combine(_tree.Root, "A"), Path.Combine(_tree.Root, "suelto.txt"), Path.Combine(_tree.Root, "no-existe")],
            new SyncProgress(reports.Add), CancellationToken.None);

        Assert.False(Directory.Exists(Path.Combine(_tree.Root, "A")));
        Assert.False(File.Exists(Path.Combine(_tree.Root, "suelto.txt")));
        Assert.Equal(4, reports[^1].Files);
    }

    [Fact]
    public void Una_union_se_quita_sin_borrar_lo_que_hay_detras()
    {
        _tree.File(@"Destino\importante.txt", 10, DateTime.Now);
        var target = Path.Combine(_tree.Root, "Destino");
        var link = Path.Combine(_tree.Root, @"Borrar\enlace");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Directory.CreateSymbolicLink(link, target);

        FileDeleter.Delete([Path.Combine(_tree.Root, "Borrar")], null, CancellationToken.None);

        Assert.False(Directory.Exists(Path.Combine(_tree.Root, "Borrar")));
        Assert.True(File.Exists(Path.Combine(target, "importante.txt")));
    }

    [Fact]
    public void Parado_antes_de_empezar_no_borra_nada()
    {
        _tree.File(@"A\uno.txt", 10, DateTime.Now);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        FileDeleter.Delete([Path.Combine(_tree.Root, "A")], null, cts.Token);
        Assert.True(File.Exists(Path.Combine(_tree.Root, @"A\uno.txt")));
    }

    private sealed class SyncProgress(Action<DeleteProgress> report) : IProgress<DeleteProgress>
    {
        public void Report(DeleteProgress value) => report(value);
    }
}
