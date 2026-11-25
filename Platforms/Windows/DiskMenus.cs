using Uninstaller.Models;
using XamlControls = Microsoft.UI.Xaml.Controls;

namespace Uninstaller.Platforms.Windows;

/// <summary>Lo que hace cada entrada de los menus del boton derecho; la pagina las envuelve para explicar los fallos.</summary>
public sealed record DiskMenuActions(
    Action<FolderNode> RefreshBranch,
    Action<string> Reveal,
    Action<string> ExplorerMenu,
    Action<string> Copy,
    Action<string> Delete,
    Action<string> Open,
    Action<OpenWithApp, string> OpenWith,
    Action<string> ChooseApp);

/// <summary>
/// Los menus del boton derecho de la utilidad de espacio en disco, con el aspecto comun
/// (<see cref="SocMenu"/>): el de una carpeta del arbol y el de un fichero.
/// </summary>
public static class DiskMenus
{
    /// <summary>Carpeta: actualizar su rama, verla en el Explorador, el menu de Windows, copiar la ruta y borrarla (no la raiz).</summary>
    public static XamlControls.MenuFlyout Folder(FolderNode node, bool isRoot, bool dark, Func<string, string> text, DiskMenuActions a)
    {
        var m = new SocMenu(dark);
        var path = node.FullPath;
        m.Flyout.Items.Add(m.Item(text("DiskRefreshBranch"), "", () => a.RefreshBranch(node)));
        m.Flyout.Items.Add(m.Separator());
        m.Flyout.Items.Add(m.Item(text("DiskOpen"), "", () => a.Reveal(path)));
        m.Flyout.Items.Add(m.Item(text("DiskMoreOptions"), "", () => a.ExplorerMenu(path)));
        m.Flyout.Items.Add(m.Item(text("DiskCopy"), "", () => a.Copy(path)));
        m.Flyout.Items.Add(m.Separator());
        m.Flyout.Items.Add(m.Item(text("DiskDelete"), "", () => a.Delete(path), enabled: !isRoot, danger: true));
        return m.Flyout;
    }

    /// <summary>
    /// Fichero: abrirlo, abrirlo con uno de los programas que Windows tiene para ese tipo (o elegir
    /// otro), verlo en el Explorador, el menu de Windows, copiar la ruta y borrarlo.
    /// </summary>
    public static XamlControls.MenuFlyout File(string path, IReadOnlyList<OpenWithApp> apps, bool dark, Func<string, string> text, DiskMenuActions a)
    {
        var m = new SocMenu(dark);
        m.Flyout.Items.Add(m.Item(text("DiskOpenFile"), "", () => a.Open(path)));
        var openWith = m.SubItem(text("DiskOpenWith"), "");
        foreach (var app in apps)
            openWith.Items.Add(m.ImageItem(app.Name, app.IconFile, "", () => a.OpenWith(app, path)));
        if (openWith.Items.Count > 0)
            openWith.Items.Add(m.Separator());
        openWith.Items.Add(m.Item(text("DiskChooseApp"), "", () => a.ChooseApp(path)));
        m.Flyout.Items.Add(openWith);
        m.Flyout.Items.Add(m.Item(text("DiskOpen"), "", () => a.Reveal(path)));
        m.Flyout.Items.Add(m.Item(text("DiskMoreOptions"), "", () => a.ExplorerMenu(path)));
        m.Flyout.Items.Add(m.Item(text("DiskCopy"), "", () => a.Copy(path)));
        m.Flyout.Items.Add(m.Separator());
        m.Flyout.Items.Add(m.Item(text("DiskDelete"), "", () => a.Delete(path), danger: true));
        return m.Flyout;
    }

    /// <summary>El primer control de tipo <typeparamref name="T"/> en el arbol visual de WinUI (el propio, o uno de dentro).</summary>
    public static T? FindNative<T>(Microsoft.UI.Xaml.DependencyObject? element) where T : Microsoft.UI.Xaml.DependencyObject
    {
        if (element is null)
            return null;
        if (element is T found)
            return found;
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < count; i++)
            if (FindNative<T>(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i)) is { } child)
                return child;
        return null;
    }
}
