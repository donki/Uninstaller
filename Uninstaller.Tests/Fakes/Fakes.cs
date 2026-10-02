using System.Net;
using Uninstaller.Models;
using Uninstaller.Services;
using Uninstaller.ViewModels;

namespace Uninstaller.Tests.Fakes;

/// <summary>Preferencias en memoria.</summary>
public sealed class MemorySettings : ISettingsService
{
    public string Language { get; set; } = "en";
    public bool ShowSystemApps { get; set; }
    public string SortMode { get; set; } = "install";
    public bool TrayOnMinimize { get; set; }
}

/// <summary>Inventario de aplicaciones en memoria: desinstalar las quita de la lista.</summary>
public sealed class FakeInventory : IAppInventoryService
{
    public List<InstalledApp> Installed { get; } = new();
    public List<InstalledApp> SystemApps { get; } = new();
    public Exception? LoadError { get; set; }
    public HashSet<string> Failing { get; } = new();
    public HashSet<string> Refused { get; } = new();
    public List<(string Package, bool Unattended)> Uninstalled { get; } = new();
    public int Loads { get; private set; }
    public TaskCompletionSource? LoadGate { get; set; }

    public async Task<IReadOnlyList<InstalledApp>> GetInstalledAppsAsync(bool includeSystem)
    {
        Loads++;
        if (LoadGate is { } gate)
            await gate.Task;
        if (LoadError is { } e)
            throw e;
        // Objetos nuevos en cada carga, como el inventario real.
        return Installed.Concat(includeSystem ? SystemApps : []).Select(Copy).ToList();
    }

    public Task<bool> UninstallAsync(string packageName, bool unattended)
    {
        if (Failing.Contains(packageName))
            throw new InvalidOperationException("no se pudo " + packageName);
        Uninstalled.Add((packageName, unattended));
        if (Refused.Contains(packageName))
            return Task.FromResult(false);
        Installed.RemoveAll(a => a.PackageName == packageName);
        return Task.FromResult(true);
    }

    private static InstalledApp Copy(InstalledApp a) => new()
    {
        PackageName = a.PackageName, Label = a.Label, IsSystem = a.IsSystem, SupportsUnattended = a.SupportsUnattended,
        Publisher = a.Publisher, InstallDate = a.InstallDate, UpdatedDate = a.UpdatedDate, SizeBytes = a.SizeBytes,
    };
}

public sealed class FakeMainView : IMainView
{
    public int Focused, Unfocused;
    public List<double> Progress { get; } = new();
    public void FocusSearch() => Focused++;
    public void UnfocusSearch() => Unfocused++;
    public Task AnimateProgressAsync(double value) { Progress.Add(value); return Task.CompletedTask; }
}

/// <summary>La pagina del espacio en disco, sin interfaz: todo en el mismo hilo.</summary>
public sealed class FakeDiskView : IDiskView
{
    public int TimersStarted, TimersStopped;
    public string? Clipboard { get; private set; }
    public bool IsDarkTheme { get; set; }
    public void RunOnUi(Action action) => action();
    public IProgress<T> CreateProgress<T>(Action<T> handler) => new SyncProgress<T>(handler);
    public IDisposable StartTimer(TimeSpan interval, Action tick)
    {
        TimersStarted++;
        tick();
        return new Stopper(this);
    }
    public Task SetClipboardTextAsync(string text) { Clipboard = text; return Task.CompletedTask; }

    private sealed class Stopper(FakeDiskView owner) : IDisposable
    {
        private bool _done;
        public void Dispose() { if (!_done) { _done = true; owner.TimersStopped++; } }
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

/// <summary>Acciones del Explorador sobre el disco de verdad (carpetas temporales), sin iconos ni UAC.</summary>
public sealed class FakeShell : IShellActions
{
    public List<DriveEntry> Drives { get; } = new();
    public List<string> Revealed { get; } = new();
    public Exception? RevealError { get; set; }
    public HashSet<string> InBin { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> BinFolders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Owners { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Risks { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Locked { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool FixWorks { get; set; } = true;
    public List<IReadOnlyList<string>> Fixed { get; } = new();
    public List<string> Recycled { get; } = new();

    public IReadOnlyList<DriveEntry> GetDrives() => Drives;
    public void RevealInExplorer(string path) { if (RevealError is { } e) throw e; Revealed.Add(path); }
    public bool MoveToRecycleBin(string path) => MoveToRecycleBin([path]).Count == 0;
    public IReadOnlyList<string> MoveToRecycleBin(IReadOnlyList<string> paths)
    {
        var failed = new List<string>();
        foreach (var p in paths)
        {
            if (Locked.Contains(p)) { failed.Add(p); continue; }
            if (Directory.Exists(p)) Directory.Delete(p, true); else File.Delete(p);
            Recycled.Add(p);
        }
        return failed;
    }
    public Task<bool> FixPermissionsAsync(IReadOnlyList<string> paths)
    {
        Fixed.Add(paths);
        if (FixWorks)
            foreach (var p in paths) Locked.Remove(p);
        return Task.FromResult(FixWorks);
    }
    public string? SystemRisk(string path) => Risks.GetValueOrDefault(path);
    public ImageSource? IconFor(string path, bool isFolder) => null;
    public bool IsInRecycleBin(string path) => InBin.Contains(path);
    public string? DisplayName(string path) => Names.GetValueOrDefault(path);
    public bool IsRecycleBinFolder(string path) => BinFolders.Contains(path);
    public string? RecycleBinOwner(string path) => Owners.GetValueOrDefault(path);
}

public sealed class FakeDesktop : IDesktopIntegration
{
    public bool StartsWithWindows { get; set; }
    public List<bool> TrayChanges { get; } = new();
    public void SetStartWithWindows(bool enabled) => StartsWithWindows = enabled;
    public void SetMinimizeToTray(bool enabled) => TrayChanges.Add(enabled);
}

/// <summary>Dialogos que contestan solos, en orden, y apuntan lo que se ha preguntado.</summary>
public sealed class ScriptedDialogs : IDialogService
{
    private readonly Queue<object?> _answers = new();

    public List<(string Kind, string? Title, string? Message, string[] Options)> Calls { get; } = new();

    public ScriptedDialogs Answer(params object?[] answers)
    {
        foreach (var a in answers)
            _answers.Enqueue(a);
        return this;
    }

    public (string Kind, string? Title, string? Message, string[] Options) Last => Calls[^1];

    public IEnumerable<string?> Messages => Calls.Select(c => c.Message);

    public Task<bool> AlertAsync(string title, string message, string accept, string? cancel = null)
    {
        Calls.Add(("alert", title, message, cancel is null ? new[] { accept } : new[] { accept, cancel }));
        if (cancel is null)
            return Task.FromResult(true);
        return Task.FromResult(_answers.Count > 0 ? (bool)_answers.Dequeue()! : true);
    }

    public Task<string?> ActionSheetAsync(string? title, string cancel, params string[] options)
    {
        Calls.Add(("sheet", title, null, options));
        return Task.FromResult(_answers.Count > 0 ? (string?)_answers.Dequeue() : cancel);
    }

    public Task<string?> PromptAsync(string title, string? message, string accept, string cancel, string? initialValue = null)
    {
        Calls.Add(("prompt", title, initialValue, Array.Empty<string>()));
        return Task.FromResult(_answers.Count > 0 ? (string?)_answers.Dequeue() : null);
    }
}

public sealed class FakeToast : IToastService
{
    public List<string> Shown { get; } = new();
    public void Show(string message) => Shown.Add(message);
}

public sealed class FakeEnvironment : IAppEnvironment
{
    public string VersionString { get; set; } = "2026.10.01.0";
    public bool EmailAvailable { get; set; } = true;
    public Exception? EmailError { get; set; }
    public List<Uri> Opened { get; } = new();
    public List<(string Subject, string To)> Emails { get; } = new();

    public Task OpenUrlAsync(Uri uri)
    {
        Opened.Add(uri);
        return Task.CompletedTask;
    }

    public Task<bool> ComposeEmailAsync(string subject, string to)
    {
        if (EmailError is { } e)
            throw e;
        Emails.Add((subject, to));
        return Task.FromResult(EmailAvailable);
    }
}

/// <summary>Respuesta HTTP fija (sin red).</summary>
public sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri?> Requests { get; } = new();

    public static StubHttpHandler Json(string json) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri);
        return Task.FromResult(respond(request));
    }
}
