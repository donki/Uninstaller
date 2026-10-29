using Uninstaller.Helpers;
using Uninstaller.Models;
using Uninstaller.Services;
using Uninstaller.ViewModels;

namespace Uninstaller.Pages;

/// <summary>
/// Espacio en disco, estilo TreeSize: enlace fino con <see cref="DiskUsageViewModel"/>, que tiene la
/// logica. Aqui solo se vuelca su estado en los controles y se le pasan los toques.
/// </summary>
public partial class DiskUsagePage : ContentPage, IDiskView
{
    private readonly ILocalizationService _l;
    private readonly DiskUsageViewModel _vm;
    private readonly IShellActions _shell;
    private int _drawnMap = -1;

    /// <summary>Ruta que llega por linea de ordenes (--disk ruta): se escanea al abrir la pagina. Vacia = solo abrir.</summary>
    public static string? PendingPath { get; set; }

    /// <summary>Con --map: la primera vista es el mapa de rectangulos.</summary>
    public static bool PendingMap { get; set; }

    public DiskUsagePage()
    {
        InitializeComponent();
        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _shell = ServiceHelper.GetRequiredService<IShellActions>();
        _vm = new DiskUsageViewModel(_l, _shell,
            ServiceHelper.GetRequiredService<IToastService>(), new PageDialogService(this), this);
        TreeList.ItemsSource = _vm.Rows;
        BindableLayout.SetItemsSource(QueueList, _vm.Queue.Jobs);
        MapView.Drawable = _vm.Map;
        _vm.Changed += (_, _) => Render();
        PathEntry.TextChanged += (_, e) => _vm.PathText = e.NewTextValue ?? string.Empty;
        _l.LanguageChanged += (_, _) => ApplyTexts();
        ApplyTexts();
        _vm.LoadDrives();
        SizeChanged += (_, _) => RelayoutLists();
        Loaded += async (_, _) =>
        {
            if (PendingPath is not { Length: > 0 } pending)
                return;
            PendingPath = null;
            PathEntry.Text = pending;
            await _vm.ScanAsync(startInMap: PendingMap);
        };
    }

    private void ApplyTexts()
    {
        Title = _l["DiskTitle"];
        PathEntry.Placeholder = _l["DiskPathPlaceholder"];
        EmptyLabel.Text = _l["DiskEmpty"];
        foreach (var (name, key) in DiskUsageViewModel.ButtonTexts)
        {
            var button = (BindableObject)FindByName(name);
            SemanticProperties.SetDescription(button, _l[key]);
            ToolTipProperties.SetText(button, _l[key]);
        }
        _vm.LanguageChanged();
    }

    /// <summary>Vuelca el estado de la logica en los controles.</summary>
    private void Render()
    {
        if (!ReferenceEquals(DrivePicker.ItemsSource, _vm.DriveLabels))
            DrivePicker.ItemsSource = _vm.DriveLabels;
        if (DrivePicker.SelectedIndex != _vm.SelectedDriveIndex)
            DrivePicker.SelectedIndex = _vm.SelectedDriveIndex;
        if (PathEntry.Text != _vm.PathText)
            PathEntry.Text = _vm.PathText;
        StatusLabel.Text = _vm.StatusText;
        ScanButton.IsEnabled = _vm.ScanEnabled;
        StopButton.IsEnabled = _vm.StopEnabled;
        ScanProgress.IsVisible = _vm.ProgressVisible;
        ScanProgress.Progress = _vm.Progress;
        QueuePanel.IsVisible = _vm.Queue.Jobs.Count > 0;
        OpenButton.IsEnabled = _vm.OpenEnabled;
        CopyButton.IsEnabled = _vm.CopyEnabled;
        DeleteButton.IsEnabled = _vm.DeleteEnabled;
        ExportButton.IsEnabled = _vm.ExportEnabled;
        UpButton.IsEnabled = _vm.UpEnabled;
        ToolTipProperties.SetText(DeleteButton, _vm.DeleteTooltip);

        var mode = _vm.Mode;
        TreeList.IsVisible = mode == DiskViewMode.Tree;
        MapPanel.IsVisible = mode == DiskViewMode.Map;
        LargestList.IsVisible = mode == DiskViewMode.Largest;
        AggregateList.IsVisible = mode is DiskViewMode.Types or DiskViewMode.Age;
        DuplicatesList.IsVisible = mode == DiskViewMode.Duplicates;
        if (!ReferenceEquals(LargestList.ItemsSource, _vm.Largest))
            LargestList.ItemsSource = _vm.Largest;
        if (!ReferenceEquals(AggregateList.ItemsSource, _vm.Aggregate))
            AggregateList.ItemsSource = _vm.Aggregate;
        if (!ReferenceEquals(DuplicatesList.ItemsSource, _vm.Duplicates))
            DuplicatesList.ItemsSource = _vm.Duplicates;

        var primary = (Color)Application.Current!.Resources["Primary"];
        foreach (var (name, viewMode, icon) in DiskUsageViewModel.ViewButtons)
        {
            var button = (Button)FindByName(name);
            var on = viewMode == mode;
            button.BackgroundColor = on ? primary : Colors.Transparent;
            button.ImageSource = on ? icon + "_w.png" : icon + ".png";
        }

        if (_drawnMap != _vm.MapVersion)
        {
            _drawnMap = _vm.MapVersion;
            MapLabel.Text = _vm.MapLabel;
            MapView.Invalidate();
        }
    }

    // ============ IDiskView ============

    public void RunOnUi(Action action) => MainThread.BeginInvokeOnMainThread(action);

    public IProgress<T> CreateProgress<T>(Action<T> handler) => new Progress<T>(handler);

    public IDisposable StartTimer(TimeSpan interval, Action tick)
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = interval;
        timer.Tick += (_, _) => tick();
        timer.Start();
        return new TimerStopper(timer);
    }

    private sealed class TimerStopper(IDispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }

    public bool IsDarkTheme => Application.Current?.RequestedTheme == AppTheme.Dark;

    public Task SetClipboardTextAsync(string text) => Clipboard.Default.SetTextAsync(text);

    // ============ Toques ============

    private void OnDriveChanged(object? sender, EventArgs e) => _vm.SelectDrive(DrivePicker.SelectedIndex);

    /// <summary>Los toques asincronos: un fallo se explica en vez de cerrar la aplicacion.</summary>
    private async void Safe(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await new PageDialogService(this).AlertAsync(_l["Error"], ex.Message, _l["Ok"]);
        }
    }

    private void OnScanClicked(object? sender, EventArgs e) => Safe(() => _vm.ScanAsync());

    private void OnStopClicked(object? sender, EventArgs e) => _vm.Stop();

    private void OnExpanderTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not FolderNode node)
            return;
#if WINDOWS
        // Al meter las filas de la rama la lista de WinUI volvia arriba: se deja donde estaba.
        var scroller = FindNative<Microsoft.UI.Xaml.Controls.ScrollViewer>(TreeList.Handler?.PlatformView as Microsoft.UI.Xaml.DependencyObject);
        var offset = scroller?.VerticalOffset;
        _vm.Toggle(node);
        if (scroller is not null && offset is { } y)
        {
            scroller.ChangeView(null, y, null, true);
            Dispatcher.Dispatch(() => scroller.ChangeView(null, y, null, true));
        }
#else
        _vm.Toggle(node);
#endif
    }

    // ============ Boton derecho ============

    /// <summary>Menu del boton derecho de una fila: una carpeta del arbol o un fichero.</summary>
    private void OnRowSecondaryTapped(object? sender, TappedEventArgs e)
    {
#if WINDOWS
        if (sender is not View view || view.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement native)
            return;
        var menu = view.BindingContext switch
        {
            FolderNode node => FolderMenu(node),
            FileRow row => FileMenu(row.FullPath),
            _ => null,
        };
        if (menu is null)
            return;
        var at = e.GetPosition(view) ?? new Point(0, 0);
        menu.ShowAt(native, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = new global::Windows.Foundation.Point(at.X, at.Y) });
#endif
    }

#if WINDOWS
    private IntPtr WindowHandle => Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window w ? WinRT.Interop.WindowNative.GetWindowHandle(w) : IntPtr.Zero;

    /// <summary>Carpeta: actualizar su rama, verla en el Explorador, el menu de Windows, copiar la ruta y borrarla.</summary>
    private Microsoft.UI.Xaml.Controls.MenuFlyout FolderMenu(FolderNode node)
    {
        var m = new Platforms.Windows.SocMenu(Platforms.Windows.SocMenu.IsDark);
        var path = node.FullPath;
        m.Flyout.Items.Add(m.Item(_l["DiskRefreshBranch"], "\uE72C", () => _vm.RefreshBranch(node)));
        m.Flyout.Items.Add(m.Separator());
        m.Flyout.Items.Add(m.Item(_l["DiskOpen"], "\uE838", () => Try(() => _shell.RevealInExplorer(path))));
        m.Flyout.Items.Add(m.Item(_l["DiskMoreOptions"], "\uE712", () => Try(() => Platforms.Windows.ShellOpen.ShowExplorerMenu(WindowHandle, path))));
        m.Flyout.Items.Add(m.Item(_l["DiskCopy"], "\uE8C8", () => Safe(() => _vm.CopyAsync([path]))));
        m.Flyout.Items.Add(m.Separator());
        m.Flyout.Items.Add(m.Item(_l["DiskDelete"], "\uE74D", () => Safe(() => _vm.DeleteAsync([path])),
            enabled: !ReferenceEquals(node, _vm.Root), danger: true));
        return m.Flyout;
    }

    /// <summary>
    /// Fichero: abrirlo, abrirlo con uno de los programas que Windows tiene para ese tipo (o elegir
    /// otro), verlo en el Explorador, el menu de Windows, copiar la ruta y borrarlo.
    /// </summary>
    private Microsoft.UI.Xaml.Controls.MenuFlyout FileMenu(string path)
    {
        var m = new Platforms.Windows.SocMenu(Platforms.Windows.SocMenu.IsDark);
        m.Flyout.Items.Add(m.Item(_l["DiskOpenFile"], "\uE8E5", () => Try(() => Platforms.Windows.ShellOpen.Open(path))));
        var openWith = m.SubItem(_l["DiskOpenWith"], "\uE7AC");
        foreach (var app in Platforms.Windows.ShellOpen.AppsFor(path))
            openWith.Items.Add(m.ImageItem(app.Name, app.IconFile, "\uE7AC", () => Try(() => app.Open(path))));
        if (openWith.Items.Count > 0)
            openWith.Items.Add(m.Separator());
        openWith.Items.Add(m.Item(_l["DiskChooseApp"], "\uE712", () => Try(() => Platforms.Windows.ShellOpen.ChooseApp(WindowHandle, path))));
        m.Flyout.Items.Add(openWith);
        m.Flyout.Items.Add(m.Item(_l["DiskOpen"], "\uE838", () => Try(() => _shell.RevealInExplorer(path))));
        m.Flyout.Items.Add(m.Item(_l["DiskMoreOptions"], "\uE712", () => Try(() => Platforms.Windows.ShellOpen.ShowExplorerMenu(WindowHandle, path))));
        m.Flyout.Items.Add(m.Item(_l["DiskCopy"], "\uE8C8", () => Safe(() => _vm.CopyAsync([path]))));
        m.Flyout.Items.Add(m.Separator());
        m.Flyout.Items.Add(m.Item(_l["DiskDelete"], "\uE74D", () => Safe(() => _vm.DeleteAsync([path])), danger: true));
        return m.Flyout;
    }

    private static T? FindNative<T>(Microsoft.UI.Xaml.DependencyObject? element) where T : Microsoft.UI.Xaml.DependencyObject
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
#endif

    /// <summary>Una accion del sistema (abrir, Explorador): si falla, se explica.</summary>
    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _ = new PageDialogService(this).AlertAsync(_l["Error"], ex.Message, _l["Ok"]);
        }
    }

    // Al pasar el raton, el desplegable se marca: se ve que ahi se puede pulsar.
    private void OnExpanderPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border { BindingContext: FolderNode { HasChildren: true } } border)
            border.BackgroundColor = ((Color)Application.Current!.Resources["Primary"]).WithAlpha(0.15f);
    }

    private void OnExpanderPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border border)
            border.BackgroundColor = Colors.Transparent;
    }

    private void OnJobCancelClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is QueuedJob job)
            _vm.Queue.Cancel(job);
    }

    private void OnJobDismissClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is QueuedJob job)
            _vm.Queue.Dismiss(job);
    }

    // ============ Redimensionar ============

    private IDispatcherTimer? _relayout;
    private int _relayoutPasses;

    /// <summary>
    /// En Windows, al maximizar (o volver de pantalla completa) las listas a veces se quedaban con el
    /// ancho de antes: el CollectionView de WinUI no vuelve a medir las filas ya creadas. Se fuerza
    /// en dos pasadas, al acabar el cambio de tamaño y un poco despues (la animacion de maximizar
    /// llega en varios tamaños seguidos).
    /// </summary>
    private void RelayoutLists()
    {
        _relayoutPasses = 2;
        if (_relayout is null)
        {
            _relayout = Dispatcher.CreateTimer();
            _relayout.Interval = TimeSpan.FromMilliseconds(150);
            _relayout.Tick += (_, _) =>
            {
                foreach (var list in new[] { TreeList, LargestList, AggregateList, DuplicatesList })
                    if (list.IsVisible)
                        Remeasure(list);
                if (--_relayoutPasses <= 0)
                    _relayout!.Stop();
            };
        }
        _relayout.Stop();
        _relayout.Start();
    }

    private static void Remeasure(CollectionView list)
    {
        foreach (var child in list.LogicalChildren.OfType<IView>())
            child.InvalidateMeasure();
        ((IView)list).InvalidateMeasure();
#if WINDOWS
        if (list.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement native)
            InvalidateNative(native);
#endif
    }

#if WINDOWS
    private static void InvalidateNative(Microsoft.UI.Xaml.DependencyObject element)
    {
        if (element is Microsoft.UI.Xaml.UIElement ui)
            ui.InvalidateMeasure();
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < count; i++)
            InvalidateNative(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i));
    }
#endif

    private void OnViewClicked(object? sender, EventArgs e)
    {
        var mode = DiskUsageViewModel.ViewButtons.FirstOrDefault(v => ReferenceEquals(FindByName(v.Button), sender)).Mode;
        Safe(() => _vm.ChooseViewAsync(mode));
    }

    private void OnMapTapped(object? sender, TappedEventArgs e)
    {
        if (e.GetPosition(MapView) is { } p)
            _vm.MapTapped(new PointF((float)p.X, (float)p.Y));
    }

    private void OnMapDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.GetPosition(MapView) is { } p)
            _vm.MapDoubleTapped(new PointF((float)p.X, (float)p.Y));
    }

    private void OnUpClicked(object? sender, EventArgs e) => _vm.MapUp();

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _vm.TreeSelected = TreeList.SelectedItem;
        _vm.LargestSelected = LargestList.SelectedItem;
        _vm.UpdateActions();
    }

    private void OnItemCheckedChanged(object? sender, CheckedChangedEventArgs e) => _vm.UpdateActions();

    private void OnDuplicateRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is FileRow row)
            _vm.SelectDuplicate(row);
    }

    private void OnOpenClicked(object? sender, EventArgs e) => Safe(_vm.OpenAsync);

    private void OnCopyClicked(object? sender, EventArgs e) => Safe(_vm.CopyAsync);

    private void OnDeleteClicked(object? sender, EventArgs e) => Safe(_vm.DeleteAsync);

    private void OnExportClicked(object? sender, EventArgs e) =>
        Safe(() => _vm.ExportAsync(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DateTime.Now));
}
