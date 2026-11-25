using System.Runtime.InteropServices;
using Microsoft.Win32;
using Uninstaller.Platforms.Windows;

namespace Uninstaller.WindowsTests;

/// <summary>«Arrancar con Windows» sobre una clave Run propia de la prueba y la identidad para anclar a la barra.</summary>
[Collection("Windows")]
public sealed class StartupAndTaskbarTests : IDisposable
{
    private readonly string _root = @"Software\sOCUninstallerTests\" + Guid.NewGuid().ToString("N");
    private readonly string _runKey = WindowsStartup.RunKey;
    private readonly string? _launcher = Environment.GetEnvironmentVariable("SOC_LAUNCHER");

    public StartupAndTaskbarTests() => WindowsStartup.RunKey = _root + @"\Run";

    public void Dispose()
    {
        WindowsStartup.RunKey = _runKey;
        Environment.SetEnvironmentVariable("SOC_LAUNCHER", _launcher);
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
    }

    [Fact]
    public void Arrancar_con_Windows_escondida_en_la_bandeja()
    {
        Assert.False(WindowsStartup.IsEnabled("sOCUninstaller"));

        WindowsStartup.Set("sOCUninstaller", true);
        Assert.True(WindowsStartup.IsEnabled("sOCUninstaller"));
        using (var key = Registry.CurrentUser.OpenSubKey(WindowsStartup.RunKey)!)
            Assert.Equal("\"" + Environment.ProcessPath + "\" --tray", key.GetValue("sOCUninstaller"));

        WindowsStartup.Set("sOCUninstaller", false);
        Assert.False(WindowsStartup.IsEnabled("sOCUninstaller"));
        WindowsStartup.Set("sOCUninstaller", false);           // quitar lo que no esta no falla
    }

    [Fact]
    public void Con_lanzador_arranca_el_lanzador()
    {
        var launcher = Path.GetTempFileName();
        try
        {
            Environment.SetEnvironmentVariable("SOC_LAUNCHER", launcher);
            Assert.Equal(launcher, WindowsStartup.ExecutablePath);
            Environment.SetEnvironmentVariable("SOC_LAUNCHER", launcher + ".no-esta");
            Assert.Equal(Environment.ProcessPath, WindowsStartup.ExecutablePath);
        }
        finally
        {
            File.Delete(launcher);
        }
    }

    [Fact]
    public void Sin_clave_valida_no_esta_activado_ni_falla()
    {
        WindowsStartup.RunKey = null!;
        Assert.False(WindowsStartup.IsEnabled("sOCUninstaller"));
        WindowsStartup.Set("sOCUninstaller", true);
    }

    [Fact]
    public void Ajustes_cambia_el_arranque_y_la_bandeja_al_vuelo()
    {
        var desktop = new Uninstaller.Services.DesktopIntegration();
        Assert.False(desktop.StartsWithWindows);
        desktop.SetStartWithWindows(true);
        Assert.True(desktop.StartsWithWindows);
        desktop.SetStartWithWindows(false);
        Assert.False(desktop.StartsWithWindows);

        // Sin icono de bandeja (aun no hay ventana) no pasa nada; con el, se aplica en el momento.
        TrayIcon.Current = null;
        desktop.SetMinimizeToTray(false);
        var hwnd = CreateWindowEx(0, "STATIC", "sOC prueba", 0x80000000, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        try
        {
            TrayIcon.Current = new TrayIcon(hwnd, k => k, () => { });
            desktop.SetMinimizeToTray(false);
            Assert.False(TrayIcon.Current.MinimizeToTray);
            desktop.SetMinimizeToTray(true);
            Assert.True(TrayIcon.Current.MinimizeToTray);
        }
        finally
        {
            TrayIcon.Current = null;
            DestroyWindow(hwnd);
        }
    }

    [Fact]
    public void Identidad_de_la_ventana_para_anclar_el_lanzador()
    {
        var hwnd = CreateWindowEx(0, "STATIC", "sOC prueba", 0x80000000 /* WS_POPUP, sin WS_VISIBLE */, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, hwnd);
        var launcher = Path.GetTempFileName();
        try
        {
            TaskbarIdentity.Apply(IntPtr.Zero, "Socratic.Uninstaller", "sOC Uninstaller", launcher);   // sin ventana: nada
            TaskbarIdentity.Apply(hwnd, "Socratic.Uninstaller.Prueba", "sOC Uninstaller", launcher);
            Assert.Equal("Socratic.Uninstaller.Prueba", ReadString(hwnd, 5));
            Assert.Equal("\"" + launcher + "\"", ReadString(hwnd, 2));
            Assert.Equal(launcher + ",0", ReadString(hwnd, 3));

            // Sin lanzador (exe suelto) solo lleva el identificador.
            TaskbarIdentity.Apply(hwnd, "Socratic.Uninstaller.Otra", "sOC Uninstaller", null);
            Assert.Equal("Socratic.Uninstaller.Otra", ReadString(hwnd, 5));
        }
        finally
        {
            DestroyWindow(hwnd);
            File.Delete(launcher);
        }
    }

    private static string? ReadString(IntPtr hwnd, uint pid)
    {
        var iid = typeof(IPropertyStore).GUID;
        Assert.Equal(0, SHGetPropertyStoreForWindow(hwnd, ref iid, out var store));
        var key = new PropertyKey { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = pid };
        store!.GetValue(ref key, out var value);
        Marshal.ReleaseComObject(store);
        return value.vt == 31 ? Marshal.PtrToStringUni(value.p) : null;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
}
