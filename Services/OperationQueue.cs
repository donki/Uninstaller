using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Uninstaller.Services;

/// <summary>
/// Una orden de la cola (papelera, borrar, permisos, exportar…): lo que se ve de ella en la lista y
/// lo que hay que hacer en segundo plano y despues, ya en el hilo de la interfaz.
/// </summary>
public sealed class QueuedJob : INotifyPropertyChanged
{
    private readonly Action<Action> _runOnUi;
    private string _detail = string.Empty;
    private double _progress;
    private bool _running, _failed;

    internal QueuedJob(string title, Action<Action> runOnUi, Func<QueuedJob, CancellationToken, Task> work, Func<QueuedJob, Task>? whenDone)
    {
        Title = title;
        _runOnUi = runOnUi;
        Work = work;
        WhenDone = whenDone;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    internal Func<QueuedJob, CancellationToken, Task> Work { get; }

    internal Func<QueuedJob, Task>? WhenDone { get; }

    internal CancellationTokenSource Cancellation { get; } = new();

    /// <summary>Que se pidio pararla (puede haber hecho parte).</summary>
    public bool CancelRequested => Cancellation.IsCancellationRequested;

    /// <summary>El error si la orden fallo entera; null si fue bien (aunque algunas rutas no se pudieran).</summary>
    public Exception? Error { get; internal set; }

    /// <summary>Por donde va: la ruta en curso o lo que fallo.</summary>
    public string Detail { get => _detail; internal set => Set(ref _detail, value, nameof(Detail)); }

    /// <summary>0..1; 0 mientras no se sabe.</summary>
    public double Progress { get => _progress; internal set => Set(ref _progress, value, nameof(Progress)); }

    public bool IsRunning
    {
        get => _running;
        internal set { Set(ref _running, value, nameof(IsRunning)); Changed(nameof(IsPending)); }
    }

    public bool IsPending => !_running && !_failed;

    /// <summary>Fallo y se queda en la lista hasta que se quite.</summary>
    public bool IsFailed
    {
        get => _failed;
        internal set { Set(ref _failed, value, nameof(IsFailed)); Changed(nameof(IsPending)); Changed(nameof(CanCancel)); }
    }

    public bool CanCancel => !_failed;

    /// <summary>Desde el trabajo en segundo plano: por donde va. Llega a la interfaz por su hilo.</summary>
    public void Report(string detail, double progress) => _runOnUi(() =>
    {
        Detail = detail;
        Progress = Math.Clamp(progress, 0, 1);
    });

    private void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        Changed(name);
    }

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Cola de ordenes que se hacen una detras de otra en segundo plano, para que se pueda seguir
/// trabajando mientras tanto (escanear, mirar otra vista, mandar mas cosas a borrar). Cada orden
/// hace su trabajo fuera del hilo de la interfaz y despues, ya en el, su remate (quitarla del
/// arbol, avisar, ofrecer permisos). Las que acaban bien desaparecen de la lista; las que fallan se
/// quedan, con el motivo, hasta que se quitan.
/// </summary>
public sealed class OperationQueue
{
    private readonly Action<Action> _runOnUi;
    private readonly Queue<QueuedJob> _pending = new();
    private TaskCompletionSource? _idle;

    /// <param name="runOnUi">Ejecuta en el hilo de la interfaz (para el progreso que llega de fuera).</param>
    public OperationQueue(Action<Action> runOnUi) => _runOnUi = runOnUi;

    /// <summary>Lo que se ve: las pendientes, la que esta en marcha y las que fallaron.</summary>
    public ObservableCollection<QueuedJob> Jobs { get; } = [];

    /// <summary>Cambio en la lista (para enseñarla u ocultarla).</summary>
    public event EventHandler? Changed;

    public bool IsBusy => _idle is not null;

    /// <summary>
    /// Pone una orden a la cola. <paramref name="work"/> corre en segundo plano;
    /// <paramref name="whenDone"/>, en el hilo desde el que se llamo, tanto si fue bien como si
    /// fallo (<see cref="QueuedJob.Error"/>) o se paro (<see cref="QueuedJob.CancelRequested"/>).
    /// Se llama desde el hilo de la interfaz.
    /// </summary>
    public QueuedJob Enqueue(string title, Func<QueuedJob, CancellationToken, Task> work, Func<QueuedJob, Task>? whenDone = null)
    {
        var job = new QueuedJob(title, _runOnUi, work, whenDone);
        _pending.Enqueue(job);
        Jobs.Add(job);
        Changed?.Invoke(this, EventArgs.Empty);
        if (_idle is null)
        {
            _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = PumpAsync();
        }
        return job;
    }

    /// <summary>Para una orden: si aun no habia empezado, ni empieza; si esta en marcha, se le pide parar.</summary>
    public void Cancel(QueuedJob job) => job.Cancellation.Cancel();

    /// <summary>Quita de la lista una orden que fallo.</summary>
    public void Dismiss(QueuedJob job)
    {
        if (!job.IsFailed)
            return;
        Jobs.Remove(job);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Termina cuando no queda nada pendiente (incluido lo que se encole en los remates).</summary>
    public Task WhenIdle() => _idle?.Task ?? Task.CompletedTask;

    private async Task PumpAsync()
    {
        while (_pending.TryDequeue(out var job))
        {
            if (!job.CancelRequested)
            {
                job.IsRunning = true;
                Changed?.Invoke(this, EventArgs.Empty);
                try
                {
                    await Task.Run(() => job.Work(job, job.Cancellation.Token));
                }
                catch (OperationCanceledException) when (job.CancelRequested)
                {
                }
                catch (Exception ex)
                {
                    job.Error = ex;
                }
                job.IsRunning = false;
            }
            try
            {
                if (job.WhenDone is not null)
                    await job.WhenDone(job);
            }
            catch (Exception ex)
            {
                job.Error ??= ex;
            }
            if (job.Error is not null && !job.CancelRequested)
            {
                job.Detail = job.Error.Message;
                job.IsFailed = true;
            }
            else
            {
                Jobs.Remove(job);
            }
            job.Cancellation.Dispose();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        var idle = _idle;
        _idle = null;
        idle?.TrySetResult();
    }
}
