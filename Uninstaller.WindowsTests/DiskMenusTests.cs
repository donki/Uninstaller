using Microsoft.UI.Xaml.Automation.Peers;
using Uninstaller.Models;
using Uninstaller.Platforms.Windows;
using XamlControls = Microsoft.UI.Xaml.Controls;

namespace Uninstaller.WindowsTests;

/// <summary>Los menus del boton derecho de la utilidad de espacio, sobre controles de WinUI de verdad, pulsando cada entrada.</summary>
[Collection("Windows")]
public sealed class DiskMenusTests
{
    private readonly List<string> _done = [];

    public DiskMenusTests() => WinUIHost.Ensure();

    private DiskMenuActions Actions() => new(
        node => _done.Add("rama " + node.Name),
        path => _done.Add("explorador " + path),
        path => _done.Add("menu " + path),
        path => _done.Add("copiar " + path),
        path => _done.Add("borrar " + path),
        path => _done.Add("abrir " + path),
        (app, path) => _done.Add(app.Name + " " + path),
        path => _done.Add("elegir " + path));

    private static string Text(string key) => "[" + key + "]";

    /// <summary>Pulsa la entrada (como el raton: Invoke del control) y deja que corra la accion, que va a la cola.</summary>
    private static void Click(XamlControls.MenuFlyoutItem item) => new MenuFlyoutItemAutomationPeer(item).Invoke();

    /// <summary>Las entradas con su texto (- para separadores), y pulsadas una a una.</summary>
    private static List<string> Describe(IList<XamlControls.MenuFlyoutItemBase> items, bool click) =>
        items.Select(i =>
        {
            switch (i)
            {
                case XamlControls.MenuFlyoutSubItem sub:
                    return sub.Text + " {" + string.Join(", ", Describe(sub.Items, click)) + "}";
                case XamlControls.MenuFlyoutItem item:
                    if (click && item.IsEnabled)
                        Click(item);
                    return item.Text + (item.IsEnabled ? "" : " (apagado)");
                default:
                    return "-";
            }
        }).ToList();

    [Fact]
    public void Menu_de_una_carpeta()
    {
        var node = new FolderNode { Name = "Fotos", FullPath = @"C:\Fotos" };
        var entries = WinUIHost.Run(() => Describe(DiskMenus.Folder(node, isRoot: false, dark: false, Text, Actions()).Items, click: true));
        WinUIHost.Run(() => 0);   // las acciones se encolan al pulsar: se espera a que corran

        Assert.Equal(["[DiskRefreshBranch]", "-", "[DiskOpen]", "[DiskMoreOptions]", "[DiskCopy]", "-", "[DiskDelete]"], entries);
        Assert.Equal(["rama Fotos", @"explorador C:\Fotos", @"menu C:\Fotos", @"copiar C:\Fotos", @"borrar C:\Fotos"], _done);
    }

    [Fact]
    public void La_raiz_no_se_puede_borrar_desde_su_menu()
    {
        var root = new FolderNode { Name = @"C:\", FullPath = @"C:\" };
        var entries = WinUIHost.Run(() => Describe(DiskMenus.Folder(root, isRoot: true, dark: true, Text, Actions()).Items, click: false));
        Assert.Equal("[DiskDelete] (apagado)", entries[^1]);
    }

    [Fact]
    public void Menu_de_un_fichero_con_sus_programas()
    {
        var apps = new[] { new OpenWithApp("Editor", null, _ => { }), new OpenWithApp("Visor", null, _ => { }) };
        var entries = WinUIHost.Run(() => Describe(DiskMenus.File(@"C:\x\nota.txt", apps, dark: false, Text, Actions()).Items, click: true));
        WinUIHost.Run(() => 0);

        Assert.Equal(["[DiskOpenFile]", "[DiskOpenWith] {Editor, Visor, -, [DiskChooseApp]}", "[DiskOpen]", "[DiskMoreOptions]", "[DiskCopy]", "-", "[DiskDelete]"], entries);
        Assert.Equal([@"abrir C:\x\nota.txt", @"Editor C:\x\nota.txt", @"Visor C:\x\nota.txt", @"elegir C:\x\nota.txt",
            @"explorador C:\x\nota.txt", @"menu C:\x\nota.txt", @"copiar C:\x\nota.txt", @"borrar C:\x\nota.txt"], _done);
    }

    [Fact]
    public void Sin_programas_para_el_tipo_solo_elegir_otro()
    {
        var entries = WinUIHost.Run(() => Describe(DiskMenus.File(@"C:\x\raro.zzz", [], dark: false, Text, Actions()).Items, click: false));
        Assert.Equal("[DiskOpenWith] {[DiskChooseApp]}", entries[1]);
    }

    [Fact]
    public void Buscar_un_control_dentro_del_arbol_de_WinUI()
    {
        var (self, inside, missing, none) = WinUIHost.Run(() =>
        {
            var scroller = new XamlControls.ScrollViewer();
            var inner = new XamlControls.StackPanel();
            inner.Children.Add(new XamlControls.TextBlock());
            inner.Children.Add(scroller);
            var outer = new XamlControls.Grid();
            outer.Children.Add(new XamlControls.Border());
            outer.Children.Add(inner);
            return (DiskMenus.FindNative<XamlControls.Grid>(outer) == outer,
                DiskMenus.FindNative<XamlControls.ScrollViewer>(outer) == scroller,
                DiskMenus.FindNative<XamlControls.ListView>(outer) is null,
                DiskMenus.FindNative<XamlControls.Grid>(null) is null);
        });
        Assert.True(self);
        Assert.True(inside);
        Assert.True(missing);
        Assert.True(none);
    }
}
