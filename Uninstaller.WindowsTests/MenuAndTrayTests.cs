using System.Reflection;
using System.Runtime.InteropServices;
using Uninstaller.Platforms.Windows;
using XamlControls = Microsoft.UI.Xaml.Controls;
using XamlMedia = Microsoft.UI.Xaml.Media;

namespace Uninstaller.WindowsTests;

/// <summary>
/// El menu comun de las apps sOC (General 6.14) sobre controles de WinUI de verdad, y el icono de la
/// bandeja sobre una ventana oculta de la prueba. No se restaura ninguna ventana ni se abre el menu:
/// las pruebas no roban el primer plano.
/// </summary>
[Collection("Windows")]
public sealed class MenuAndTrayTests
{
    public MenuAndTrayTests() => WinUIHost.Ensure();

    [Fact]
    public void Elementos_con_las_medidas_y_colores_comunes()
    {
        var (text, opacity, enabled, glyph, font, minHeight, pointerOver, disabledText, disabledOpacity, dangerColor) = WinUIHost.Run(() =>
        {
            var menu = new SocMenu(dark: false);
            var item = menu.Item("Abrir", "", () => { });
            var disabled = menu.Item("Borrar", "", () => { }, enabled: false, danger: true);
            var icon = (XamlControls.FontIcon)item.Icon;
            var danger = (XamlMedia.SolidColorBrush)((XamlControls.FontIcon)disabled.Icon).Foreground;
            return (item.Text, item.Opacity, item.IsEnabled, icon.Glyph, icon.FontSize, item.MinHeight,
                item.Resources.ContainsKey("MenuFlyoutItemBackgroundPointerOver"), disabled.Text, disabled.Opacity, danger.Color);
        });
        Assert.Equal("Abrir", text);
        Assert.Equal(1, opacity);
        Assert.True(enabled);
        Assert.Equal("", glyph);
        Assert.Equal(14, font);
        Assert.Equal(32, minHeight);
        Assert.True(pointerOver);
        Assert.Equal("Borrar", disabledText);
        Assert.Equal(0.45, disabledOpacity, 3);
        // Sin la App de MAUI no hay colores de Resources: el gris de reserva.
        Assert.Equal(global::Windows.UI.Color.FromArgb(255, 128, 128, 128), dangerColor);
    }

    [Fact]
    public void Submenu_separador_e_iconos_de_programas()
    {
        var png = Path.Combine(Path.GetTempPath(), $"uninst-icono-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
        try
        {
            var r = WinUIHost.Run(() =>
            {
                var menu = new SocMenu(dark: true);
                var sub = menu.SubItem("Abrir con", "");
                sub.Items.Add(menu.ImageItem("Programa", png, "", () => { }));
                sub.Items.Add(menu.ImageItem("Sin icono", null, "", () => { }));
                sub.Items.Add(menu.Separator());
                menu.Flyout.Items.Add(sub);
                var withIcon = (XamlControls.MenuFlyoutItem)sub.Items[0];
                var without = (XamlControls.MenuFlyoutItem)sub.Items[1];
                var separator = (XamlControls.MenuFlyoutSeparator)sub.Items[2];
                return (sub.Text, Items: menu.Flyout.Items.Count, SubItems: sub.Items.Count,
                    Image: withIcon.Icon is XamlControls.ImageIcon image && image.Width == 16,
                    Fallback: without.Icon is XamlControls.FontIcon,
                    SeparatorHeight: separator.Resources["MenuFlyoutSeparatorThemeHeight"],
                    Presenter: sub.Resources.ContainsKey("MenuFlyoutPresenterStyle"),
                    Constrained: menu.Flyout.ShouldConstrainToRootBounds,
                    Style: menu.Flyout.MenuFlyoutPresenterStyle.Setters.Count);
            });
            Assert.Equal("Abrir con", r.Text);
            Assert.Equal(1, r.Items);
            Assert.Equal(3, r.SubItems);
            Assert.True(r.Image);
            Assert.True(r.Fallback);
            Assert.Equal(1d, r.SeparatorHeight);
            Assert.True(r.Presenter);
            Assert.False(r.Constrained);
            Assert.Equal(8, r.Style);
            Assert.False(SocMenu.IsDark);   // sin App de MAUI: tema claro
        }
        finally
        {
            File.Delete(png);
        }
    }

    [Fact]
    public void Icono_de_la_bandeja_al_minimizar_y_al_salir()
    {
        var hwnd = CreateWindowEx(0x80 /* WS_EX_TOOLWINDOW */, "STATIC", "sOC prueba", 0x80000000 /* WS_POPUP, oculta */, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, hwnd);
        var exited = 0;
        try
        {
            var tray = new TrayIcon(hwnd, key => "[" + key + "]", () => exited++);
            var shown = typeof(TrayIcon).GetField("_shown", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.True(tray.MinimizeToTray);

            // Mensajes que no son suyos siguen al procedimiento de la ventana.
            Assert.Equal(IntPtr.Zero, SendMessage(hwnd, 0x0000 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero));
            // Minimizar (SC_MINIMIZE) la esconde en la bandeja.
            Assert.Equal(IntPtr.Zero, SendMessage(hwnd, 0x0112, (IntPtr)0xF020, IntPtr.Zero));
            Assert.True((bool)shown.GetValue(tray)!);
            tray.HideToTray();                    // ya esta: no se añade otra vez
            // En el icono, mover el raton no hace nada.
            Assert.Equal(IntPtr.Zero, SendMessage(hwnd, 0x8001, IntPtr.Zero, (IntPtr)0x0200));

            // El menu de la bandeja: Abrir, separador y Salir, con los textos traducidos.
            var build = typeof(TrayIcon).GetMethod("BuildMenu", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var labels = WinUIHost.Run(() =>
            {
                var menu = (XamlControls.MenuFlyout)build.Invoke(tray, [false])!;
                return menu.Items.Select(i => (i as XamlControls.MenuFlyoutItem)?.Text ?? "-").ToList();
            });
            Assert.Equal(["[TrayOpen]", "-", "[TrayExit]"], labels);

            // Salir quita el icono y cierra la aplicacion.
            typeof(TrayIcon).GetMethod("Exit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(tray, null);
            Assert.False((bool)shown.GetValue(tray)!);
            Assert.Equal(1, exited);

            // El icono: el del ejecutable o, si no tiene, el generico de aplicacion.
            Assert.NotEqual(IntPtr.Zero, (IntPtr)typeof(TrayIcon).GetMethod("LoadAppIcon", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!);
        }
        finally
        {
            DestroyWindow(hwnd);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
