using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Dispatching;
using Uninstaller.Helpers;
using Uninstaller.Services;
using Uninstaller.Tests.Fakes;

namespace Uninstaller.Tests;

[CollectionDefinition("Paginas", DisableParallelization = true)]
public sealed class PaginasCollection;

/// <summary>
/// Las paginas de verdad (XAML compilado y code-behind) sin plataforma debajo: la App con sus estilos,
/// los servicios dobles en el contenedor de <see cref="ServiceHelper"/> y un Dispatcher que ejecuta
/// en el acto. Las pruebas pulsan los controles como lo haria el usuario.
/// </summary>
public abstract class PageHost : IDisposable
{
    private readonly CultureInfo? _culture = CultureInfo.DefaultThreadCurrentCulture;
    private readonly CultureInfo? _uiCulture = CultureInfo.DefaultThreadCurrentUICulture;

    protected readonly MemorySettings Settings = new();
    protected readonly LocalizationService Loc;
    protected readonly FakeInventory Inventory = new();
    protected readonly FakeToast Toast = new();
    protected readonly FakeShell Shell = new();
    protected readonly FakeDesktop Desktop = new();
    protected readonly FakeEnvironment Environment = new();
    protected readonly TestDispatcher Dispatcher = new();

    protected PageHost()
    {
        Preferences.Clear();
        Loc = new LocalizationService(Settings, NullLogger<LocalizationService>.Instance);
        var services = new ServiceCollection()
            .AddSingleton<ISettingsService>(Settings)
            .AddSingleton<ILocalizationService>(Loc)
            .AddSingleton<IAppInventoryService>(Inventory)
            .AddSingleton<IToastService>(Toast)
            .AddSingleton<IShellActions>(Shell)
            .AddSingleton<IDesktopIntegration>(Desktop)
            .AddSingleton<IAppEnvironment>(Environment)
            // Sin red: la comprobacion de version recibe siempre «estas al dia».
            .AddSingleton(new UpdateService(Loc, Environment, new HttpClient(StubHttpHandler.Json("{\"version\":\"2000.1.1.0\"}"))))
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .BuildServiceProvider();
        ServiceHelper.Initialize(services);
        DispatcherProvider.SetCurrent(new TestDispatcherProvider(Dispatcher));
        Application.Current = new App();
        // Las animaciones (dialogos, barra de progreso) buscan su gestor en la App: uno real, mil veces
        // mas rapido, para que terminen en el primer golpe de reloj.
        var context = new MauiContext(new ServiceCollection()
            .AddSingleton<Microsoft.Maui.Animations.IAnimationManager>(
                new Microsoft.Maui.Animations.AnimationManager(new Microsoft.Maui.Animations.Ticker()) { SpeedModifier = 1000 })
            .BuildServiceProvider());
        var handler = new TestAppHandler();
        handler.SetMauiContext(context);
        Application.Current.Handler = handler;
    }

    protected string L(string key) => Loc[key];

    /// <summary>Un control de la pagina por su x:Name.</summary>
    protected static T Named<T>(Element page, string name) where T : class =>
        page.FindByName(name) as T ?? throw new InvalidOperationException($"No hay {typeof(T).Name} '{name}'.");

    /// <summary>Pulsa un boton como el usuario: dispara su Clicked.</summary>
    protected static void Click(Element page, string name)
    {
        switch (page.FindByName(name))
        {
            case Button b: ((IButtonController)b).SendClicked(); break;
            case ImageButton i: ((IButtonController)i).SendClicked(); break;
            default: throw new InvalidOperationException($"'{name}' no es un boton.");
        }
    }

    /// <summary>Llama a un manejador privado del code-behind (toques sin control con nombre: filas, mapa).</summary>
    protected static object? Invoke(object target, string handler, params object?[] args) =>
        target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Single(m => m.Name == handler && m.GetParameters().Length == args.Length)
            .Invoke(target, args);

    /// <summary>
    /// Contesta el dialogo que la pagina tiene abierto (los de SocShared.ModernDialog son una capa
    /// dentro de la propia pagina) pulsando el boton con ese texto. Devuelve el texto del dialogo.
    /// </summary>
    protected static async Task<string> AnswerDialog(ContentPage page, string option)
    {
        for (var i = 0; i < 100; i++)
        {
            var overlay = (page.Content as Grid)?.Children.OfType<Grid>().FirstOrDefault(g => g.StyleId == "__modernDialogOverlay");
            if (overlay?.GetVisualTreeDescendants().OfType<Button>().FirstOrDefault(b => b.Text == option) is { } button)
            {
                var text = string.Join(" | ", overlay.GetVisualTreeDescendants().OfType<Label>().Select(l => l.Text));
                ((IButtonController)button).SendClicked();
                await Settle();
                return text;
            }
            await Task.Delay(10);
        }
        throw new InvalidOperationException($"No hay dialogo con la opcion '{option}'.");
    }

    /// <summary>Hay un dialogo abierto en la pagina.</summary>
    protected static bool DialogOpen(ContentPage page) =>
        (page.Content as Grid)?.Children.OfType<Grid>().Any(g => g.StyleId == "__modernDialogOverlay") == true;

    /// <summary>Deja correr lo que el code-behind lanzo sin esperar (async void).</summary>
    protected static async Task Settle()
    {
        for (var i = 0; i < 5; i++)
            await Task.Delay(10);
    }

    public virtual void Dispose()
    {
        Application.Current = null;
        CultureInfo.DefaultThreadCurrentCulture = _culture;
        CultureInfo.DefaultThreadCurrentUICulture = _uiCulture;
    }
}

/// <summary>Dispatcher de prueba: ejecuta en el acto; los temporizadores solo avanzan con <see cref="TickAll"/>.</summary>
public sealed class TestDispatcher : IDispatcher
{
    public List<TestTimer> Timers { get; } = new();

    public bool IsDispatchRequired => false;

    public bool Dispatch(Action action)
    {
        action();
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action) => Dispatch(action);

    public IDispatcherTimer CreateTimer()
    {
        var timer = new TestTimer();
        Timers.Add(timer);
        return timer;
    }

    /// <summary>Un golpe de reloj en todos los temporizadores en marcha.</summary>
    public void TickAll()
    {
        foreach (var t in Timers.Where(t => t.IsRunning).ToList())
            t.Fire();
    }
}

public sealed class TestTimer : IDispatcherTimer
{
    public TimeSpan Interval { get; set; }
    public bool IsRepeating { get; set; } = true;
    public bool IsRunning { get; private set; }
    public event EventHandler? Tick;
    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;
    public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
}

/// <summary>Manejador de la App sin plataforma: solo aporta el MauiContext con el gestor de animaciones.</summary>
public sealed class TestAppHandler() : Microsoft.Maui.Handlers.ElementHandler<IApplication, object>(new PropertyMapper<IApplication, TestAppHandler>())
{
    protected override object CreatePlatformElement() => new();
}

public sealed class TestDispatcherProvider(IDispatcher dispatcher) : IDispatcherProvider
{
    public IDispatcher? GetForCurrentThread() => dispatcher;
}
