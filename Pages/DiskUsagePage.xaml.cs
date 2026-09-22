using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using SocShared;
using Uninstaller.Helpers;
using Uninstaller.Models;
using Uninstaller.Services;

namespace Uninstaller.Pages;

/// <summary>
/// Espacio en disco, estilo TreeSize: se escanea una unidad, una carpeta o una ruta de red y se ve
/// el arbol de carpetas con lo que ocupa cada una; ademas, los ficheros mas grandes, el reparto por
/// tipo y por antigüedad y los ficheros duplicados. Sobre lo elegido: abrir en el Explorador,
/// copiar la ruta y mandar a la papelera. Code-behind delgado: el trabajo lo hace DiskScanner.
/// </summary>
public partial class DiskUsagePage : ContentPage
{
    private enum ViewMode { Tree, Largest, Types, Age, Duplicates, Map }

    private readonly ILocalizationService _l;
    private readonly IShellActions _shell;
    private readonly IToastService _toast;
    private IReadOnlyList<DriveEntry> _drives = [];
    private FolderNode? _root;
    private CancellationTokenSource? _scan;
    private ViewMode _mode = ViewMode.Tree;
    private List<DuplicateGroup>? _duplicates;
    private readonly ObservableCollection<FolderNode> _rows = [];
    private FileRow? _selectedDuplicate;
    private readonly TreemapDrawable _map = new();
    private FolderNode? _mapRoot;
    private FolderNode? _mapSelected;

    /// <summary>Ruta que llega por linea de ordenes (--disk ruta): se escanea al abrir la pagina. Vacia = solo abrir.</summary>
    public static string? PendingPath { get; set; }

    /// <summary>Con --map: la primera vista es el mapa de rectangulos.</summary>
    public static bool PendingMap { get; set; }

    public DiskUsagePage()
    {
        InitializeComponent();
        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _shell = ServiceHelper.GetRequiredService<IShellActions>();
        _toast = ServiceHelper.GetRequiredService<IToastService>();
        TreeList.ItemsSource = _rows;
        _map.FormatSize = FormatSize;
        MapView.Drawable = _map;
        _l.LanguageChanged += (_, _) => ApplyTexts();
        ApplyTexts();
        LoadDrives();
        HighlightView();
        Loaded += (_, _) =>
        {
            if (PendingPath is not { Length: > 0 } pending)
                return;
            PendingPath = null;
            PathEntry.Text = pending;
            if (PendingMap)
                _mode = ViewMode.Map;
            OnScanClicked(this, EventArgs.Empty);
        };
    }

    private void ApplyTexts()
    {
        Title = _l["DiskTitle"];
        PathEntry.Placeholder = _l["DiskPathPlaceholder"];
        EmptyLabel.Text = _l["DiskEmpty"];
        if (_root is null)
            StatusLabel.Text = _l["DiskHint"];
        foreach (var (button, key) in new[] { (ScanButton, "DiskScan"), (StopButton, "DiskStop"), (TreeButton, "DiskTree"), (LargestButton, "DiskLargest"),
                     (TypesButton, "DiskTypes"), (AgeButton, "DiskAge"), (DuplicatesButton, "DiskDuplicates"), (MapButton, "DiskMap"), (OpenButton, "DiskOpen"),
                     (CopyButton, "DiskCopy"), (DeleteButton, "DiskDelete"), (ExportButton, "DiskExport"), (UpButton, "DiskUp") })
        {
            SemanticProperties.SetDescription(button, _l[key]);
            ToolTipProperties.SetText(button, _l[key]);
        }
    }

    private void LoadDrives()
    {
        _drives = _shell.GetDrives();
        DrivePicker.ItemsSource = _drives.Select(d => d.TotalBytes > 0
            ? $"{d.Label} · {FormatSize(d.TotalBytes - d.FreeBytes)} / {FormatSize(d.TotalBytes)}"
            : d.Label).ToList();
        if (_drives.Count > 0)
            DrivePicker.SelectedIndex = 0;
    }

    private void OnDriveChanged(object? sender, EventArgs e)
    {
        if (DrivePicker.SelectedIndex >= 0 && DrivePicker.SelectedIndex < _drives.Count)
            PathEntry.Text = _drives[DrivePicker.SelectedIndex].Path;
    }

    // ------------------------------------------------------------------ escaneo

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        var path = (PathEntry.Text ?? string.Empty).Trim().Trim('"');
        if (path.Length == 0)
            return;
        if (_scan is not null)
            return;

        _scan = new CancellationTokenSource();
        ScanButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        ScanProgress.IsVisible = true;
        ScanProgress.Progress = 0;
        _root = null;
        _duplicates = null;
        _mapRoot = null;
        _mapSelected = null;
        _rows.Clear();
        LargestList.ItemsSource = null;
        AggregateList.ItemsSource = null;
        DuplicatesList.ItemsSource = null;
        UpdateActions();
        var started = DateTime.Now;
        var progress = new Progress<ScanProgress>(p =>
            StatusLabel.Text = string.Format(_l.CurrentCulture, _l["DiskScanning"], p.Folders, p.Files, FormatSize(p.Bytes), p.Current));
        try
        {
            _root = await DiskScanner.ScanAsync(path, progress, _scan.Token);
            _root.IsExpanded = true;
            Describe(_root);
            RebuildRows();
            StatusLabel.Text = string.Format(_l.CurrentCulture, _l["DiskDone"], _root.FullPath, FormatSize(_root.Size), _root.FileCount, _root.FolderCount, (DateTime.Now - started).TotalSeconds.ToString("0.0", _l.CurrentCulture));
            ShowView(_mode == ViewMode.Duplicates ? ViewMode.Tree : _mode);
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = _l["DiskCancelled"];
        }
        catch (Exception ex)
        {
            StatusLabel.Text = _l["DiskHint"];
            await ModernDialog.AlertAsync(this, _l["Error"], string.Format(_l.CurrentCulture, _l["DiskScanError"], path, ex.Message), _l["Ok"]);
        }
        finally
        {
            _scan.Dispose();
            _scan = null;
            ScanButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            ScanProgress.IsVisible = false;
            UpdateActions();
        }
    }

    private void OnStopClicked(object? sender, EventArgs e) => _scan?.Cancel();

    /// <summary>Textos de la fila de una carpeta y de todas las que cuelgan (una vez, tras escanear).</summary>
    private void Describe(FolderNode node)
    {
        var culture = _l.CurrentCulture;
        var dateFormat = culture.DateTimeFormat.ShortDatePattern.Replace("yyyy", "yy");
        var stack = new Stack<FolderNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            n.SizeText = FormatSize(n.Size);
            var modified = n.LastModified == DateTime.MinValue ? "—" : n.LastModified.ToString(dateFormat, culture);
            n.DetailText = string.Format(culture, _l["DiskFolderDetail"], (n.Percent * 100).ToString("0.#", culture), n.FileCount, n.FolderCount, modified)
                           + (n.Inaccessible ? " · " + _l["DiskInaccessible"] : string.Empty);
            foreach (var c in n.Children)
                stack.Push(c);
        }
    }

    // ------------------------------------------------------------------ arbol aplanado

    private void RebuildRows()
    {
        _rows.Clear();
        if (_root is null)
            return;
        var list = new List<FolderNode>();
        Flatten(_root, list);
        foreach (var n in list)
            _rows.Add(n);
    }

    private static void Flatten(FolderNode node, List<FolderNode> into)
    {
        into.Add(node);
        if (!node.IsExpanded)
            return;
        foreach (var c in node.Children)
            Flatten(c, into);
    }

    private void OnExpanderTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not FolderNode node || !node.HasChildren)
            return;
        Toggle(node);
    }

    private void Toggle(FolderNode node)
    {
        var index = _rows.IndexOf(node);
        if (index < 0)
            return;
        if (node.IsExpanded)
        {
            // Se quitan todas las filas que cuelgan de ella (las que tienen mas profundidad a continuacion).
            node.IsExpanded = false;
            while (index + 1 < _rows.Count && _rows[index + 1].Depth > node.Depth)
                _rows.RemoveAt(index + 1);
        }
        else
        {
            node.IsExpanded = true;
            var list = new List<FolderNode>();
            foreach (var c in node.Children)
                Flatten(c, list);
            for (var i = 0; i < list.Count; i++)
                _rows.Insert(index + 1 + i, list[i]);
        }
    }

    // ------------------------------------------------------------------ vistas

    private async void OnViewClicked(object? sender, EventArgs e)
    {
        var mode = ReferenceEquals(sender, LargestButton) ? ViewMode.Largest
            : ReferenceEquals(sender, TypesButton) ? ViewMode.Types
            : ReferenceEquals(sender, AgeButton) ? ViewMode.Age
            : ReferenceEquals(sender, DuplicatesButton) ? ViewMode.Duplicates
            : ReferenceEquals(sender, MapButton) ? ViewMode.Map
            : ViewMode.Tree;
        if (mode == ViewMode.Duplicates && _root is not null && _duplicates is null)
            await FindDuplicatesAsync();
        ShowView(mode);
    }

    private void ShowView(ViewMode mode)
    {
        _mode = mode;
        if (_root is not null)
        {
            switch (mode)
            {
                case ViewMode.Largest:
                    LargestList.ItemsSource ??= BuildLargest();
                    break;
                case ViewMode.Types:
                    AggregateList.ItemsSource = BuildTypes();
                    break;
                case ViewMode.Age:
                    AggregateList.ItemsSource = BuildAge();
                    break;
                case ViewMode.Duplicates:
                    DuplicatesList.ItemsSource = _duplicates;
                    break;
                case ViewMode.Map:
                    _mapRoot ??= _root;
                    RedrawMap();
                    break;
            }
        }
        TreeList.IsVisible = mode == ViewMode.Tree;
        MapPanel.IsVisible = mode == ViewMode.Map;
        UpButton.IsEnabled = mode == ViewMode.Map && _mapRoot?.Parent is not null;
        LargestList.IsVisible = mode == ViewMode.Largest;
        AggregateList.IsVisible = mode is ViewMode.Types or ViewMode.Age;
        DuplicatesList.IsVisible = mode == ViewMode.Duplicates;
        HighlightView();
        UpdateActions();
    }

    private void HighlightView()
    {
        var primary = (Color)Application.Current!.Resources["Primary"];
        foreach (var (button, mode, icon) in new[] { (TreeButton, ViewMode.Tree, "ic_tree"), (LargestButton, ViewMode.Largest, "ic_biggest"),
                     (TypesButton, ViewMode.Types, "ic_types"), (AgeButton, ViewMode.Age, "ic_clock"), (DuplicatesButton, ViewMode.Duplicates, "ic_duplicates"),
                     (MapButton, ViewMode.Map, "ic_treemap") })
        {
            var on = mode == _mode;
            button.BackgroundColor = on ? primary : Colors.Transparent;
            button.ImageSource = on ? icon + "_w.png" : icon + ".png";
        }
    }

    private List<FileRow> BuildLargest()
    {
        var culture = _l.CurrentCulture;
        var dateFormat = culture.DateTimeFormat.ShortDatePattern;
        var top = _root!.AllFiles().OrderByDescending(f => f.Size).Take(300).ToList();
        var max = top.Count > 0 ? top[0].Size : 1;
        return top.Select(f => new FileRow(f, FormatSize(f.Size), f.Modified.ToString(dateFormat, culture), f.Size / (double)max)).ToList();
    }

    private List<AggregateRow> BuildTypes()
    {
        var culture = _l.CurrentCulture;
        var total = Math.Max(1, _root!.Size);
        return _root.AllFiles()
            .GroupBy(f => f.Extension.Length > 0 ? f.Extension : _l["DiskNoExtension"])
            .Select(g => (Label: g.Key, Size: g.Sum(f => f.Size), Count: g.Count()))
            .OrderByDescending(g => g.Size)
            .Take(200)
            .Select(g => new AggregateRow(g.Label, g.Size, g.Count, g.Size / (double)total, FormatSize(g.Size),
                string.Format(culture, _l["DiskAggregateDetail"], (g.Size * 100.0 / total).ToString("0.#", culture), g.Count)))
            .ToList();
    }

    private List<AggregateRow> BuildAge()
    {
        var culture = _l.CurrentCulture;
        var total = Math.Max(1, _root!.Size);
        var now = DateTime.Now;
        var buckets = new (string Key, Func<TimeSpan, bool> Match)[]
        {
            ("DiskAge1", a => a.TotalDays < 30),
            ("DiskAge2", a => a.TotalDays < 180),
            ("DiskAge3", a => a.TotalDays < 365),
            ("DiskAge4", a => a.TotalDays < 730),
            ("DiskAge5", _ => true),
        };
        var sums = new long[buckets.Length];
        var counts = new int[buckets.Length];
        foreach (var f in _root.AllFiles())
        {
            var age = now - f.Modified;
            for (var i = 0; i < buckets.Length; i++)
            {
                if (buckets[i].Match(age))
                {
                    sums[i] += f.Size;
                    counts[i]++;
                    break;
                }
            }
        }
        return buckets.Select((b, i) => new AggregateRow(_l[b.Key], sums[i], counts[i], sums[i] / (double)total, FormatSize(sums[i]),
            string.Format(culture, _l["DiskAggregateDetail"], (sums[i] * 100.0 / total).ToString("0.#", culture), counts[i]))).ToList();
    }

    private async Task FindDuplicatesAsync()
    {
        if (_root is null || _scan is not null)
            return;
        _scan = new CancellationTokenSource();
        ScanButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        ScanProgress.IsVisible = true;
        var progress = new Progress<ScanProgress>(p =>
        {
            StatusLabel.Text = string.Format(_l.CurrentCulture, _l["DiskHashing"], p.Files, p.Folders, p.Current);
            ScanProgress.Progress = p.Folders > 0 ? p.Files / (double)p.Folders : 0;
        });
        try
        {
            var groups = await DiskScanner.FindDuplicatesAsync(_root.AllFiles(), 64 * 1024, progress, _scan.Token);
            var culture = _l.CurrentCulture;
            var dateFormat = culture.DateTimeFormat.ShortDatePattern;
            foreach (var g in groups)
            {
                g.Title = string.Format(culture, _l["DiskDuplicateTitle"], g.Files[0].Name, g.Files.Count, FormatSize(g.Size));
                g.Detail = string.Format(culture, _l["DiskDuplicateDetail"], FormatSize(g.Wasted));
                g.Rows.AddRange(g.Files.Select(f => new FileRow(f, FormatSize(f.Size), f.Modified.ToString(dateFormat, culture), 1)));
            }
            _duplicates = groups;
            var wasted = groups.Sum(g => g.Wasted);
            StatusLabel.Text = string.Format(culture, _l["DiskDuplicatesDone"], groups.Count, FormatSize(wasted));
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = _l["DiskCancelled"];
        }
        finally
        {
            _scan.Dispose();
            _scan = null;
            ScanButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            ScanProgress.IsVisible = false;
        }
    }

    // ------------------------------------------------------------------ mapa

    private void RedrawMap()
    {
        _map.Root = _mapRoot;
        _map.Selected = _mapSelected;
        _map.Dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        MapLabel.Text = _mapRoot is null ? string.Empty
            : _mapSelected is null || ReferenceEquals(_mapSelected, _mapRoot)
                ? $"{_mapRoot.FullPath} · {FormatSize(_mapRoot.Size)}"
                : $"{_mapSelected.FullPath} · {FormatSize(_mapSelected.Size)} · {(_mapSelected.Percent * 100).ToString("0.#", _l.CurrentCulture)} %";
        MapView.Invalidate();
        UpButton.IsEnabled = _mapRoot?.Parent is not null;
    }

    private void OnMapTapped(object? sender, TappedEventArgs e)
    {
        if (e.GetPosition(MapView) is not { } p)
            return;
        _mapSelected = _map.HitTest(new PointF((float)p.X, (float)p.Y));
        RedrawMap();
        UpdateActions();
    }

    private void OnMapDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.GetPosition(MapView) is not { } p)
            return;
        var hit = _map.HitTest(new PointF((float)p.X, (float)p.Y));
        if (hit is null || !hit.HasChildren)
            return;
        _mapRoot = hit;
        _mapSelected = null;
        RedrawMap();
        UpdateActions();
    }

    private void OnUpClicked(object? sender, EventArgs e)
    {
        if (_mapRoot?.Parent is null)
            return;
        _mapSelected = _mapRoot;
        _mapRoot = _mapRoot.Parent;
        RedrawMap();
        UpdateActions();
    }

    // ------------------------------------------------------------------ seleccion y acciones

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateActions();

    private void OnItemCheckedChanged(object? sender, CheckedChangedEventArgs e) => UpdateActions();

    /// <summary>
    /// Lo marcado con las casillas en la vista actual. En el arbol, si una carpeta marcada cuelga de
    /// otra tambien marcada, se queda solo la de arriba (la papelera se lleva todo lo de dentro).
    /// </summary>
    private List<string> CheckedPaths()
    {
        switch (_mode)
        {
            case ViewMode.Tree:
            {
                if (_root is null)
                    return [];
                var result = new List<string>();
                var stack = new Stack<FolderNode>();
                stack.Push(_root);
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    if (n.IsChecked && !ReferenceEquals(n, _root))
                    {
                        result.Add(n.FullPath);
                        continue;   // lo de dentro va con ella
                    }
                    foreach (var c in n.Children)
                        stack.Push(c);
                }
                return result;
            }
            case ViewMode.Largest:
                return ((IEnumerable<FileRow>?)LargestList.ItemsSource ?? []).Where(r => r.IsChecked).Select(r => r.FullPath).ToList();
            case ViewMode.Duplicates:
                return (_duplicates ?? []).SelectMany(g => g.Rows).Where(r => r.IsChecked).Select(r => r.FullPath).ToList();
            default:
                return [];
        }
    }

    /// <summary>Sobre lo que actuan los botones: lo marcado si hay algo marcado; si no, lo elegido.</summary>
    private List<string> TargetPaths()
    {
        var checkedPaths = CheckedPaths();
        if (checkedPaths.Count > 0)
            return checkedPaths;
        return SelectedPath() is { } single ? [single] : [];
    }

    private void OnDuplicateRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not FileRow row)
            return;
        if (_selectedDuplicate is not null)
            _selectedDuplicate.IsSelected = false;
        _selectedDuplicate = row;
        row.IsSelected = true;
        UpdateActions();
    }

    /// <summary>La ruta elegida en la vista actual, o null.</summary>
    private string? SelectedPath() => _mode switch
    {
        ViewMode.Tree => (TreeList.SelectedItem as FolderNode)?.FullPath,
        ViewMode.Largest => (LargestList.SelectedItem as FileRow)?.FullPath,
        ViewMode.Duplicates => _selectedDuplicate?.FullPath,
        ViewMode.Map => (_mapSelected ?? _mapRoot)?.FullPath,
        _ => null,
    };

    private void UpdateActions()
    {
        var checkedCount = CheckedPaths().Count;
        var has = SelectedPath() is not null;
        OpenButton.IsEnabled = has;
        CopyButton.IsEnabled = has || checkedCount > 0;
        // La raiz escaneada no se manda a la papelera desde aqui.
        DeleteButton.IsEnabled = checkedCount > 0
                                 || (has && !(_mode == ViewMode.Tree && ReferenceEquals(TreeList.SelectedItem, _root))
                                         && !(_mode == ViewMode.Map && ReferenceEquals(_mapSelected ?? _mapRoot, _root)));
        ExportButton.IsEnabled = _root is not null && _scan is null;
        ToolTipProperties.SetText(DeleteButton, checkedCount > 1 ? string.Format(_l.CurrentCulture, _l["DiskDeleteMany"], checkedCount) : _l["DiskDelete"]);
    }

    private async void OnOpenClicked(object? sender, EventArgs e)
    {
        if (SelectedPath() is not { } path)
            return;
        try { _shell.RevealInExplorer(path); }
        catch (Exception ex) { await ModernDialog.AlertAsync(this, _l["Error"], ex.Message, _l["Ok"]); }
    }

    private async void OnCopyClicked(object? sender, EventArgs e)
    {
        var paths = TargetPaths();
        if (paths.Count == 0)
            return;
        await Clipboard.Default.SetTextAsync(string.Join(Environment.NewLine, paths));
        _toast.Show(paths.Count > 1 ? string.Format(_l.CurrentCulture, _l["DiskCopiedMany"], paths.Count) : _l["DiskCopied"]);
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        var paths = TargetPaths();
        if (paths.Count == 0)
            return;
        var culture = _l.CurrentCulture;
        string message;
        if (paths.Count == 1)
        {
            var isFolder = Directory.Exists(paths[0]);
            message = string.Format(culture, _l[isFolder ? "DiskDeleteFolderConfirm" : "DiskDeleteFileConfirm"], paths[0]);
        }
        else
        {
            // Varios: cuantos son, cuanto ocupan y los primeros, para que se vea que es lo que va.
            var total = paths.Sum(SizeOf);
            var shown = string.Join(Environment.NewLine, paths.Take(8));
            if (paths.Count > 8)
                shown += Environment.NewLine + string.Format(culture, _l["DiskAndMore"], paths.Count - 8);
            message = string.Format(culture, _l["DiskDeleteManyConfirm"], paths.Count, FormatSize(total), shown);
        }
        var confirm = await ModernDialog.AlertAsync(this, _l["DiskDelete"], message, _l["DiskDelete"], _l["Cancel"]);
        if (!confirm)
            return;
        // A la papelera en segundo plano y con el aviso a la vista: una carpeta grande tarda, y
        // Windows enseña ademas su propio dialogo de progreso. Varios van en una sola operacion.
        Busy(true, paths.Count == 1 ? string.Format(culture, _l["DiskDeleting"], paths[0]) : string.Format(culture, _l["DiskDeletingMany"], paths.Count));
        IReadOnlyList<string> failed;
        try { failed = await Task.Run(() => _shell.MoveToRecycleBin(paths)); }
        finally { Busy(false, string.Empty); }
        var done = paths.Where(p => !failed.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
        foreach (var p in done)
            RemoveFromModel(p, refresh: false);
        if (done.Count > 0)
            RefreshAfterRemove();
        if (failed.Count > 0)
        {
            var detail = failed.Count == 1 && paths.Count == 1
                ? string.Format(culture, _l["DiskDeleteFailed"], failed[0])
                : string.Format(culture, _l["DiskDeleteFailedMany"], failed.Count, string.Join(Environment.NewLine, failed.Take(8)));
            await ModernDialog.AlertAsync(this, _l["Error"], detail, _l["Ok"]);
        }
        if (done.Count > 0)
            _toast.Show(done.Count > 1 ? string.Format(culture, _l["DiskDeletedMany"], done.Count) : _l["DiskDeleted"]);
    }

    /// <summary>Lo que ocupa una ruta segun el arbol escaneado (carpeta o fichero); 0 si no esta.</summary>
    private long SizeOf(string path)
    {
        if (_root is null)
            return 0;
        if (FindNode(_root, path) is { } node)
            return node.Size;
        var owner = FindNode(_root, Path.GetDirectoryName(path) ?? string.Empty);
        return owner?.Files.FirstOrDefault(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase))?.Size ?? 0;
    }

    private void Busy(bool on, string text)
    {
        BusyLabel.Text = text;
        BusyOverlay.IsVisible = on;
        ScanButton.IsEnabled = !on;
        DeleteButton.IsEnabled = !on && DeleteButton.IsEnabled;
    }

    /// <summary>Tras borrar: se quita del arbol y se restan los tamaños hacia arriba, sin volver a escanear.</summary>
    private void RemoveFromModel(string path, bool refresh = true)
    {
        if (_root is null)
            return;
        // Carpeta
        var node = FindNode(_root, path);
        if (node is not null && node.Parent is not null)
        {
            node.Parent.Children.Remove(node);
            for (var p = node.Parent; p is not null; p = p.Parent)
            {
                p.Size -= node.Size;
                p.FileCount -= node.FileCount;
                p.FolderCount -= node.FolderCount + 1;
            }
        }
        else
        {
            // Fichero
            var owner = FindNode(_root, Path.GetDirectoryName(path) ?? string.Empty);
            var file = owner?.Files.FirstOrDefault(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase));
            if (owner is null || file is null)
                return;
            owner.Files.Remove(file);
            for (var p = owner; p is not null; p = p.Parent)
            {
                p.Size -= file.Size;
                p.FileCount -= 1;
            }
        }
        if (refresh)
            RefreshAfterRemove();
    }

    /// <summary>Vuelve a pintar las vistas con el arbol ya recortado (una vez, aunque se hayan borrado varios).</summary>
    private void RefreshAfterRemove()
    {
        if (_root is null)
            return;
        Describe(_root);
        RebuildRows();
        LargestList.ItemsSource = null;
        _duplicates = null;
        _selectedDuplicate = null;
        if (_mapRoot is not null && FindNode(_root, _mapRoot.FullPath) is null)
            _mapRoot = _root;
        _mapSelected = null;
        ShowView(_mode == ViewMode.Duplicates ? ViewMode.Tree : _mode);
    }

    private static FolderNode? FindNode(FolderNode root, string path)
    {
        if (string.Equals(root.FullPath.TrimEnd('\\', '/'), path.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return root;
        foreach (var c in root.Children)
        {
            if (path.StartsWith(c.FullPath, StringComparison.OrdinalIgnoreCase) && FindNode(c, path) is { } found)
                return found;
        }
        return null;
    }

    // ------------------------------------------------------------------ exportar

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        if (_root is null)
            return;
        var sb = new StringBuilder();
        switch (_mode)
        {
            case ViewMode.Largest:
                sb.AppendLine("Ruta;Bytes;Modificado");
                foreach (var f in _root.AllFiles().OrderByDescending(f => f.Size).Take(300))
                    sb.AppendLine($"{Csv(f.FullPath)};{f.Size};{f.Modified:yyyy-MM-dd HH:mm}");
                break;
            case ViewMode.Types:
            case ViewMode.Age:
                sb.AppendLine("Grupo;Bytes;Ficheros");
                foreach (var r in (IEnumerable<AggregateRow>?)AggregateList.ItemsSource ?? [])
                    sb.AppendLine($"{Csv(r.Label)};{r.Size};{r.Count}");
                break;
            case ViewMode.Duplicates:
                sb.AppendLine("Grupo;Ruta;Bytes;Modificado");
                var n = 0;
                foreach (var g in _duplicates ?? [])
                {
                    n++;
                    foreach (var f in g.Files)
                        sb.AppendLine($"{n};{Csv(f.FullPath)};{f.Size};{f.Modified:yyyy-MM-dd HH:mm}");
                }
                break;
            default:
                sb.AppendLine("Carpeta;Bytes;Ficheros;Carpetas;Modificado");
                var all = new List<FolderNode>();
                var stack = new Stack<FolderNode>();
                stack.Push(_root);
                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    all.Add(node);
                    foreach (var c in node.Children)
                        stack.Push(c);
                }
                foreach (var node in all.OrderByDescending(x => x.Size))
                    sb.AppendLine($"{Csv(node.FullPath)};{node.Size};{node.FileCount};{node.FolderCount};{node.LastModified:yyyy-MM-dd HH:mm}");
                break;
        }
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var file = Path.Combine(folder, $"sOCUninstaller-espacio-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        Busy(true, string.Format(_l.CurrentCulture, _l["DiskExporting"], file));
        try
        {
            await File.WriteAllTextAsync(file, sb.ToString(), new UTF8Encoding(true));
            Busy(false, string.Empty);
            _toast.Show(string.Format(_l.CurrentCulture, _l["DiskExported"], file));
            _shell.RevealInExplorer(file);
        }
        catch (Exception ex)
        {
            Busy(false, string.Empty);
            await ModernDialog.AlertAsync(this, _l["Error"], ex.Message, _l["Ok"]);
        }
    }

    private static string Csv(string s) => s.Contains(';') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    private string FormatSize(long bytes)
    {
        var culture = _l.CurrentCulture;
        const long Kb = 1024, Mb = Kb * 1024, Gb = Mb * 1024, Tb = Gb * 1024;
        return bytes switch
        {
            <= 0 => "0",
            >= Tb => $"{(bytes / (double)Tb).ToString("0.##", culture)} TB",
            >= Gb => $"{(bytes / (double)Gb).ToString("0.##", culture)} GB",
            >= Mb => $"{(bytes / (double)Mb).ToString("0.#", culture)} MB",
            >= Kb => $"{(bytes / (double)Kb).ToString("0.#", culture)} kB",
            _ => $"{bytes.ToString(culture)} B",
        };
    }
}
