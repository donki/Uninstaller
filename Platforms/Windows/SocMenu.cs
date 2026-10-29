using Xaml = Microsoft.UI.Xaml;
using XamlControls = Microsoft.UI.Xaml.Controls;
using XamlMedia = Microsoft.UI.Xaml.Media;

namespace Uninstaller.Platforms.Windows;

/// <summary>
/// Menu con el aspecto comun de las apps sOC (General 6.14, el de sOC ScreenShare), sobre el
/// MenuFlyout de WinUI: tarjeta con borde de 1 px, esquinas de 10, relleno de 4, 240 de ancho y sin
/// sombra; elementos de 32 px con icono plano de 14 px en el color de acento (rojo si es destructivo)
/// y el fondo de seleccion al pasar el raton. Lo usan el menu de la bandeja y el del boton derecho.
/// </summary>
public sealed class SocMenu
{
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    private readonly bool _dark;
    private readonly XamlMedia.Brush _card, _separator, _text, _selection;
    private readonly Xaml.Style _presenter;

    public SocMenu(bool dark)
    {
        _dark = dark;
        _card = Brush(dark ? "CardBackgroundDark" : "CardBackgroundLight");
        _separator = Brush(dark ? "SeparatorDark" : "SeparatorLight");
        _text = Brush(dark ? "TextPrimaryDark" : "TextPrimaryLight");
        _selection = Brush(dark ? "SelectionSurfaceDark" : "SelectionSurfaceLight");
        _presenter = PresenterStyle(220);
        Flyout = new XamlControls.MenuFlyout { MenuFlyoutPresenterStyle = PresenterStyle(240), ShouldConstrainToRootBounds = false };
    }

    public static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;

    public XamlControls.MenuFlyout Flyout { get; }

    /// <summary>Elemento con glifo de Segoe Fluent Icons.</summary>
    public XamlControls.MenuFlyoutItem Item(string label, string glyph, Action action, bool enabled = true, bool danger = false)
    {
        var item = Style(new XamlControls.MenuFlyoutItem(), label, enabled);
        item.Icon = Glyph(glyph, danger);
        // La accion, cuando el menu ya se ha cerrado (puede abrir un dialogo o cerrar la ventana).
        item.Click += (_, _) => Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() => action());
        return item;
    }

    /// <summary>Elemento con el icono de un programa (un PNG en disco); sin icono, el glifo de reserva.</summary>
    public XamlControls.MenuFlyoutItem ImageItem(string label, string? iconFile, string fallbackGlyph, Action action)
    {
        var item = Item(label, fallbackGlyph, action);
        if (iconFile is not null)
            item.Icon = new XamlControls.ImageIcon { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconFile)), Width = 16, Height = 16 };
        return item;
    }

    /// <summary>Submenu: se abre a la derecha, con el mismo contenedor (220 de ancho).</summary>
    public XamlControls.MenuFlyoutSubItem SubItem(string label, string glyph)
    {
        var sub = Style(new XamlControls.MenuFlyoutSubItem(), label, true);
        sub.Icon = Glyph(glyph, false);
        sub.Resources["MenuFlyoutSubItemBackground"] = new XamlMedia.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        sub.Resources["MenuFlyoutSubItemBackgroundPointerOver"] = _selection;
        sub.Resources["MenuFlyoutSubItemBackgroundPressed"] = _selection;
        sub.Resources["MenuFlyoutSubItemBackgroundSubMenuOpened"] = _selection;
        sub.Resources["MenuFlyoutSubItemForeground"] = _text;
        sub.Resources["MenuFlyoutSubItemForegroundPointerOver"] = _text;
        sub.Resources["MenuFlyoutSubItemForegroundSubMenuOpened"] = _text;
        sub.Resources["MenuFlyoutPresenterStyle"] = _presenter;
        return sub;
    }

    /// <summary>Separador: 1 px del color de separador con margenes de 8·4.</summary>
    public XamlControls.MenuFlyoutSeparator Separator()
    {
        var separator = new XamlControls.MenuFlyoutSeparator { Padding = new Xaml.Thickness(8, 4, 8, 4) };
        separator.Resources["MenuFlyoutSeparatorBackground"] = _separator;
        separator.Resources["MenuFlyoutSeparatorThemeHeight"] = 1d;
        return separator;
    }

    private T Style<T>(T item, string label, bool enabled) where T : XamlControls.MenuFlyoutItemBase
    {
        item.MinHeight = 32;
        item.Padding = new Xaml.Thickness(8, 7, 10, 7);
        item.CornerRadius = new Xaml.CornerRadius(6);
        item.IsEnabled = enabled;
        item.Opacity = enabled ? 1 : 0.45;
        switch (item)
        {
            case XamlControls.MenuFlyoutItem i:
                i.Text = label;
                i.FontSize = 13;
                i.Foreground = _text;
                // Colores de los estados del propio control (estilo ligero de WinUI): los del tema de la app.
                i.Resources["MenuFlyoutItemBackground"] = new XamlMedia.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                i.Resources["MenuFlyoutItemBackgroundPointerOver"] = _selection;
                i.Resources["MenuFlyoutItemBackgroundPressed"] = _selection;
                i.Resources["MenuFlyoutItemBackgroundDisabled"] = _card;
                i.Resources["MenuFlyoutItemForeground"] = _text;
                i.Resources["MenuFlyoutItemForegroundPointerOver"] = _text;
                i.Resources["MenuFlyoutItemForegroundPressed"] = _text;
                i.Resources["MenuFlyoutItemForegroundDisabled"] = _text;
                break;
            case XamlControls.MenuFlyoutSubItem s:
                s.Text = label;
                s.FontSize = 13;
                s.Foreground = _text;
                break;
        }
        return item;
    }

    private XamlControls.FontIcon Glyph(string glyph, bool danger) => new()
    {
        Glyph = glyph,
        FontFamily = new XamlMedia.FontFamily(IconFont),
        FontSize = 14,
        Foreground = Brush(danger ? "Danger" : _dark ? "PrimaryTextDark" : "PrimaryTextLight"),
    };

    private Xaml.Style PresenterStyle(double minWidth)
    {
        var style = new Xaml.Style(typeof(XamlControls.MenuFlyoutPresenter));
        style.Setters.Add(new Xaml.Setter(XamlControls.Control.BackgroundProperty, _card));
        style.Setters.Add(new Xaml.Setter(XamlControls.Control.BorderBrushProperty, _separator));
        style.Setters.Add(new Xaml.Setter(XamlControls.Control.BorderThicknessProperty, new Xaml.Thickness(1)));
        style.Setters.Add(new Xaml.Setter(XamlControls.Control.CornerRadiusProperty, new Xaml.CornerRadius(10)));
        style.Setters.Add(new Xaml.Setter(XamlControls.Control.PaddingProperty, new Xaml.Thickness(4)));
        style.Setters.Add(new Xaml.Setter(Xaml.FrameworkElement.MinWidthProperty, minWidth));
        style.Setters.Add(new Xaml.Setter(Xaml.FrameworkElement.RequestedThemeProperty, _dark ? Xaml.ElementTheme.Dark : Xaml.ElementTheme.Light));
        style.Setters.Add(new Xaml.Setter(XamlControls.MenuFlyoutPresenter.IsDefaultShadowEnabledProperty, false));
        return style;
    }

    /// <summary>Un color de Resources/Styles/Colors.xaml como pincel de WinUI.</summary>
    public static XamlMedia.SolidColorBrush Brush(string key)
    {
        var c = Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color ? color : Colors.Gray;
        return new XamlMedia.SolidColorBrush(global::Windows.UI.Color.FromArgb(
            (byte)(c.Alpha * 255), (byte)(c.Red * 255), (byte)(c.Green * 255), (byte)(c.Blue * 255)));
    }
}
