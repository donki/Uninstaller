using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;

namespace Uninstaller.WindowsTests;

/// <summary>
/// MAUI en Windows prepara sus manejadores de WinUI la primera vez que se usa cualquier elemento
/// (tambien un ImageSource), y eso pide un hilo con XAML. Se hace una vez, en un hilo STA propio con
/// su cola de mensajes, antes de la primera prueba que lo necesite; <see cref="Run{T}"/> ejecuta en
/// ese hilo lo que crea controles de WinUI (menus).
/// </summary>
internal static class WinUIHost
{
    private static DispatcherQueue? _queue;
    private static readonly Lazy<Exception?> Ready = new(Start);

    public static void Ensure()
    {
        if (Ready.Value is { } error)
            throw new InvalidOperationException("No se ha podido preparar WinUI para las pruebas.", error);
    }

    /// <summary>Ejecuta en el hilo de XAML y devuelve el resultado (o relanza su excepcion).</summary>
    public static T Run<T>(Func<T> work)
    {
        Ensure();
        T result = default!;
        Exception? error = null;
        using var done = new ManualResetEventSlim();
        Assert.True(_queue!.TryEnqueue(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
            done.Set();
        }));
        Assert.True(done.Wait(TimeSpan.FromSeconds(30)), "El hilo de XAML no ha respondido.");
        if (error is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }

    private static Exception? Start()
    {
        Exception? error = null;
        var done = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                var controller = DispatcherQueueController.CreateOnCurrentThread();
                _queue = controller.DispatcherQueue;
                Microsoft.UI.Xaml.Hosting.WindowsXamlManager.InitializeForCurrentThread();
                RuntimeHelpers.RunClassConstructor(typeof(Element).TypeHandle);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            done.Set();
            if (error is null)
                RunMessageLoop();   // el hilo de XAML atiende la cola mientras duren las pruebas
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        done.Wait();
        return error;
    }

    private static void RunMessageLoop()
    {
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Msg { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x, y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Msg msg);
}
