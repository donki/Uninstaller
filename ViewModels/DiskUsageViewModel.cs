using System.Collections.ObjectModel;
using System.Text;
using Uninstaller.Models;
using Uninstaller.Services;

namespace Uninstaller.ViewModels;

public enum DiskViewMode { Tree, Largest, Types, Age, Duplicates, Map }

/// <summary>Lo que el espacio en disco necesita de la pagina y del sistema.</summary>
public interface IDiskView
{
    /// <summary>Ejecuta en el hilo de la interfaz.</summary>
    void RunOnUi(Action action);

    /// <summary>Progreso que avisa en el hilo de la interfaz.</summary>
    IProgress<T> CreateProgress<T>(Action<T> handler);

    /// <summary>Arranca un temporizador; al liberarlo se para.</summary>
    IDisposable StartTimer(TimeSpan interval, Action tick);

    bool IsDarkTheme { get; }

    Task SetClipboardTextAsync(string text);
}

/// <summary>
/// Logica del espacio en disco, estilo TreeSize: escanear una unidad, carpeta o ruta de red; el arbol
/// de carpetas, los ficheros mas grandes, el reparto por tipo y por antiguedad, los duplicados y el
/// mapa de rectangulos; y sobre lo elegido, abrir en el Explorador, copiar la ruta, mandar a la
/// papelera y exportar. La pagina solo vuelca este estado (<see cref="Changed"/>) y pasa los toques.
/// </summary>
public class DiskUsageViewModel
{
    /// <summary>Botones de solo icono (x:Name) y clave de su descripcion y su ayuda contextual.</summary>
    public static readonly IReadOnlyDictionary<string, string> ButtonTexts = new Dictionary<string, string>
    {
        ["ScanButton"] = "DiskScan", ["StopButton"] = "DiskStop", ["TreeButton"] = "DiskTree",
        ["LargestButton"] = "DiskLargest", ["TypesButton"] = "DiskTypes", ["AgeButton"] = "DiskAge",
        ["DuplicatesButton"] = "DiskDuplicates", ["MapButton"] = "DiskMap", ["OpenButton"] = "DiskOpen",
        ["CopyButton"] = "DiskCopy", ["DeleteButton"] = "DiskDelete", ["ExportButton"] = "DiskExport",
        ["UpButton"] = "DiskUp",
    };

    /// <summary>Boton de cada vista y su icono (sin sufijo; el activo lleva «_w»).</summary>
    public static readonly IReadOnlyList<(string Button, DiskViewMode Mode, string Icon)> ViewButtons = new[]
    {
        ("TreeButton", DiskViewMode.Tree, "ic_tree"), ("LargestButton", DiskViewMode.Largest, "ic_biggest"),
        ("TypesButton", DiskViewMode.Types, "ic_types"), ("AgeButton", DiskViewMode.Age, "ic_clock"),
        ("DuplicatesButton", DiskViewMode.Duplicates, "ic_duplicates"), ("MapButton", DiskViewMode.Map, "ic_treemap"),
    };

    private readonly ILocalizationService _l;
    private readonly IShellActions _shell;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly IDiskView _view;
    private CancellationTokenSource? _scan;
    private FileRow? _selectedDuplicate;

    public DiskUsageViewModel(ILocalizationService localization, IShellActions shell, IToastService toast,
        IDialogService dialogs, IDiskView view)
    {
        _l = localization;
        _shell = shell;
        _toast = toast;
        _dialogs = dialogs;
        _view = view;
        Map.FormatSize = FormatSize;
        StatusText = _l["DiskHint"];
    }

    public event EventHandler? Changed;

    // ---------- Estado ----------

    public IReadOnlyList<DriveEntry> Drives { get; private set; } = [];

    public List<string> DriveLabels { get; private set; } = [];

    public int SelectedDriveIndex { get; private set; } = -1;

    public string PathText { get; set; } = string.Empty;

    public FolderNode? Root { get; private set; }

    public DiskViewMode Mode { get; private set; } = DiskViewMode.Tree;

    public bool IsScanning => _scan is not null;

    /// <summary>Arbol aplanado: las carpetas desplegadas, en orden.</summary>
    public ObservableCollection<FolderNode> Rows { get; } = [];

    public List<FileRow>? Largest { get; private set; }

    public List<AggregateRow>? Aggregate { get; private set; }

    public List<DuplicateGroup>? Duplicates { get; private set; }

    public TreemapDrawable Map { get; } = new();

    public FolderNode? MapRoot { get; private set; }

    public FolderNode? MapSelected { get; private set; }

    public string MapLabel { get; private set; } = string.Empty;

    /// <summary>Sube cada vez que hay que repintar el mapa.</summary>
    public int MapVersion { get; private set; }

    /// <summary>Lo elegido en el arbol y en la lista de los mayores (lo pone la pagina).</summary>
    public object? TreeSelected { get; set; }

    public object? LargestSelected { get; set; }

    // ---------- Lo que se ve ----------

    public string StatusText { get; private set; }

    public bool ScanEnabled { get; private set; } = true;

    public bool StopEnabled { get; private set; }

    public bool ProgressVisible { get; private set; }

    public double Progress { get; private set; }

    public bool BusyVisible { get; private set; }

    public string BusyText { get; private set; } = string.Empty;

    public bool OpenEnabled { get; private set; }

    public bool CopyEnabled { get; private set; }

    public bool DeleteEnabled { get; private set; }

    public bool ExportEnabled { get; private set; }

    public bool UpEnabled { get; private set; }

    public string DeleteTooltip { get; private set; } = string.Empty;

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    public string FormatSize(long bytes) => ListRules.DiskSize(bytes, _l.CurrentCulture);

    /// <summary>Cambio de idioma: sin nada escaneado, la pista vuelve a su idioma.</summary>
    public void LanguageChanged()
    {
        if (Root is null)
            StatusText = _l["DiskHint"];
        UpdateActions();
    }

    // ============ Unidades ============

    public void LoadDrives()
    {
        Drives = _shell.GetDrives();
        DriveLabels = Drives.Select(d => d.TotalBytes > 0
            ? $"{d.Label} · {FormatSize(d.TotalBytes - d.FreeBytes)} / {FormatSize(d.TotalBytes)}"
            : d.Label).ToList();
        if (Drives.Count > 0)
            SelectDrive(0);
        Notify();
    }

    public void SelectDrive(int index)
    {
        SelectedDriveIndex = index;
        if (index >= 0 && index < Drives.Count)
            PathText = Drives[index].Path;
        Notify();
    }

    // ============ Escaneo ============

    /// <summary>Escanea lo escrito en la ruta. <paramref name="startInMap"/>: al acabar, el mapa.</summary>
    public async Task ScanAsync(bool startInMap = false)
    {
        var path = (PathText ?? string.Empty).Trim().Trim('"');
        if (path.Length == 0 || _scan is not null)
            return;
        if (startInMap)
            Mode = DiskViewMode.Map;

        _scan = new CancellationTokenSource();
        ScanEnabled = false;
        StopEnabled = true;
        ProgressVisible = true;
        Progress = 0;
        Root = null;
        Duplicates = null;
        MapRoot = null;
        MapSelected = null;
        Rows.Clear();
        Largest = null;
        Aggregate = null;
        UpdateActions();
        var started = DateTime.Now;
        var progress = _view.CreateProgress<ScanProgress>(p =>
        {
            StatusText = string.Format(_l.CurrentCulture, _l["DiskScanning"], p.Folders, p.Files, FormatSize(p.Bytes), p.Current);
            Notify();
        });
        // Mientras se escanea, el arbol se ve crecer: la raiz llega nada mas empezar y cada poco se
        // repintan las filas visibles con lo acumulado hasta el momento.
        IDisposable? live = null;
        if (Mode is not (DiskViewMode.Tree or DiskViewMode.Map))
            ShowView(DiskViewMode.Tree);
        try
        {
            Root = await DiskScanner.ScanAsync(path, progress, _scan.Token, root => _view.RunOnUi(() =>
            {
                Root = root;
                RefreshLive();
                live ??= _view.StartTimer(TimeSpan.FromMilliseconds(600), RefreshLive);
            }));
            live?.Dispose();
            Describe(Root);
            RebuildRows();
            StatusText = string.Format(_l.CurrentCulture, _l["DiskDone"], Root.FullPath, FormatSize(Root.Size), Root.FileCount, Root.FolderCount, (DateTime.Now - started).TotalSeconds.ToString("0.0", _l.CurrentCulture));
            ShowView(Mode == DiskViewMode.Duplicates ? DiskViewMode.Tree : Mode);
        }
        catch (OperationCanceledException)
        {
            // Lo escaneado hasta ahora se queda a la vista (con los acumulados que hubiera).
            live?.Dispose();
            if (Root is not null)
            {
                Describe(Root);
                RebuildRows();
            }
            StatusText = _l["DiskCancelled"];
        }
        catch (Exception ex)
        {
            live?.Dispose();
            Root = null;
            Rows.Clear();
            StatusText = _l["DiskHint"];
            await _dialogs.AlertAsync(_l["Error"], string.Format(_l.CurrentCulture, _l["DiskScanError"], path, ex.Message), _l["Ok"]);
        }
        finally
        {
            live?.Dispose();
            _scan.Dispose();
            _scan = null;
            ScanEnabled = true;
            StopEnabled = false;
            ProgressVisible = false;
            UpdateActions();
        }
    }

    public void Stop() => _scan?.Cancel();

    /// <summary>Textos de la fila de una carpeta y de todas las que cuelgan.</summary>
    private void Describe(FolderNode node)
    {
        var stack = new Stack<FolderNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            DescribeOne(n);
            foreach (var c in n.Children)
                stack.Push(c);
        }
    }

    public void DescribeOne(FolderNode n)
    {
        var culture = _l.CurrentCulture;
        var dateFormat = ListRules.CompactDatePattern(culture);
        n.Icon ??= _shell.IconFor(n.FullPath, isFolder: true);
        // El nombre del sistema (Papelera de reciclaje, Archivos de programa, Usuarios…): se pregunta
        // una sola vez por carpeta, y solo por las que estan a la vista.
        if (!n.DisplayResolved)
        {
            n.DisplayResolved = true;
            if (_shell.IsRecycleBinFolder(n.FullPath))
                n.Display = _shell.RecycleBinOwner(n.FullPath) is { Length: > 0 } owner
                    ? _l["DiskRecycleBin"] + " · " + owner
                    : _l["DiskRecycleBin"];
            else if (_shell.DisplayName(n.FullPath) is { Length: > 0 } display)
                n.Display = display;
        }
        n.NotifyChildrenChanged();
        n.SizeText = FormatSize(n.Size);
        var modified = n.LastModified == DateTime.MinValue ? "—" : n.LastModified.ToString(dateFormat, culture);
        n.DetailText = string.Format(culture, _l["DiskFolderDetail"], (n.Percent * 100).ToString("0.#", culture), n.FileCount, n.FolderCount, modified)
                       + (n.Inaccessible ? " · " + _l["DiskInaccessible"] : string.Empty);
    }

    /// <summary>
    /// Durante el escaneo: se vuelven a aplanar las carpetas desplegadas y se repintan los textos de
    /// las filas a la vista. Solo se toca la coleccion si cambio la lista.
    /// </summary>
    public void RefreshLive()
    {
        if (Root is null)
            return;
        var list = new List<FolderNode>();
        ListRules.Flatten(Root, list);
        var same = list.Count == Rows.Count;
        for (var i = 0; same && i < list.Count; i++)
            same = ReferenceEquals(list[i], Rows[i]);
        if (!same)
        {
            Rows.Clear();
            foreach (var n in list)
                Rows.Add(n);
        }
        foreach (var n in list)
            DescribeOne(n);
        if (Mode == DiskViewMode.Map)
        {
            MapRoot ??= Root;
            RedrawMap();
        }
        Notify();
    }

    private void RebuildRows()
    {
        Rows.Clear();
        if (Root is null)
            return;
        var list = new List<FolderNode>();
        ListRules.Flatten(Root, list);
        foreach (var n in list)
            Rows.Add(n);
    }

    /// <summary>Despliega o pliega una carpeta del arbol.</summary>
    public void Toggle(FolderNode node)
    {
        var index = Rows.IndexOf(node);
        if (index < 0 || !node.HasChildren)
            return;
        if (node.IsExpanded)
        {
            // Se quitan todas las filas que cuelgan de ella (las de mas profundidad a continuacion).
            node.IsExpanded = false;
            while (index + 1 < Rows.Count && Rows[index + 1].Depth > node.Depth)
                Rows.RemoveAt(index + 1);
        }
        else
        {
            node.IsExpanded = true;
            var list = new List<FolderNode>();
            foreach (var c in node.Children)
                ListRules.Flatten(c, list);
            for (var i = 0; i < list.Count; i++)
                Rows.Insert(index + 1 + i, list[i]);
        }
    }

    // ============ Vistas ============

    /// <summary>Boton de vista pulsado: los duplicados se buscan la primera vez que se piden.</summary>
    public async Task ChooseViewAsync(DiskViewMode mode)
    {
        if (mode == DiskViewMode.Duplicates && Root is not null && Duplicates is null)
            await FindDuplicatesAsync();
        ShowView(mode);
    }

    public void ShowView(DiskViewMode mode)
    {
        Mode = mode;
        if (Root is not null)
        {
            switch (mode)
            {
                case DiskViewMode.Largest:
                    Largest ??= BuildLargest();
                    break;
                case DiskViewMode.Types:
                    Aggregate = BuildTypes();
                    break;
                case DiskViewMode.Age:
                    Aggregate = BuildAge();
                    break;
                case DiskViewMode.Map:
                    MapRoot ??= Root;
                    RedrawMap();
                    break;
            }
        }
        UpEnabled = mode == DiskViewMode.Map && MapRoot?.Parent is not null;
        UpdateActions();
    }

    private List<FileRow> BuildLargest()
    {
        var culture = _l.CurrentCulture;
        var dateFormat = culture.DateTimeFormat.ShortDatePattern;
        return ListRules.Largest(Root!.AllFiles(), 300).Select(t => new FileRow(t.File, FormatSize(t.File.Size), t.File.Modified.ToString(dateFormat, culture), t.Percent)
        {
            Icon = _shell.IconFor(t.File.FullPath, isFolder: false),
            Display = _shell.DisplayName(t.File.FullPath) is { Length: > 0 } name ? name : t.File.Name,
        }).ToList();
    }

    private List<AggregateRow> BuildTypes()
    {
        var culture = _l.CurrentCulture;
        var total = Math.Max(1, Root!.Size);
        return ListRules.ByType(Root.AllFiles(), _l["DiskNoExtension"], 200)
            .Select(g => new AggregateRow(g.Label, g.Size, g.Count, g.Size / (double)total, FormatSize(g.Size),
                string.Format(culture, _l["DiskAggregateDetail"], (g.Size * 100.0 / total).ToString("0.#", culture), g.Count)))
            .ToList();
    }

    private List<AggregateRow> BuildAge()
    {
        var culture = _l.CurrentCulture;
        var total = Math.Max(1, Root!.Size);
        var buckets = new[] { "DiskAge1", "DiskAge2", "DiskAge3", "DiskAge4", "DiskAge5" };
        var (sums, counts) = ListRules.ByAge(Root.AllFiles(), DateTime.Now);
        return buckets.Select((b, i) => new AggregateRow(_l[b], sums[i], counts[i], sums[i] / (double)total, FormatSize(sums[i]),
            string.Format(culture, _l["DiskAggregateDetail"], (sums[i] * 100.0 / total).ToString("0.#", culture), counts[i]))).ToList();
    }

    private async Task FindDuplicatesAsync()
    {
        if (Root is null || _scan is not null)
            return;
        _scan = new CancellationTokenSource();
        ScanEnabled = false;
        StopEnabled = true;
        ProgressVisible = true;
        Notify();
        var progress = _view.CreateProgress<ScanProgress>(p =>
        {
            StatusText = string.Format(_l.CurrentCulture, _l["DiskHashing"], p.Files, p.Folders, p.Current);
            Progress = p.Folders > 0 ? p.Files / (double)p.Folders : 0;
            Notify();
        });
        try
        {
            var groups = await DiskScanner.FindDuplicatesAsync(Root.AllFiles(), 64 * 1024, progress, _scan.Token);
            var culture = _l.CurrentCulture;
            var dateFormat = culture.DateTimeFormat.ShortDatePattern;
            foreach (var g in groups)
            {
                g.Title = string.Format(culture, _l["DiskDuplicateTitle"], g.Files[0].Name, g.Files.Count, FormatSize(g.Size));
                g.Detail = string.Format(culture, _l["DiskDuplicateDetail"], FormatSize(g.Wasted));
                g.Rows.AddRange(g.Files.Select(f => new FileRow(f, FormatSize(f.Size), f.Modified.ToString(dateFormat, culture), 1) { Icon = _shell.IconFor(f.FullPath, isFolder: false) }));
            }
            Duplicates = groups;
            StatusText = string.Format(culture, _l["DiskDuplicatesDone"], groups.Count, FormatSize(groups.Sum(g => g.Wasted)));
        }
        catch (OperationCanceledException)
        {
            StatusText = _l["DiskCancelled"];
        }
        finally
        {
            _scan.Dispose();
            _scan = null;
            ScanEnabled = true;
            StopEnabled = false;
            ProgressVisible = false;
            Notify();
        }
    }

    // ============ Mapa ============

    private void RedrawMap()
    {
        Map.Root = MapRoot;
        Map.Selected = MapSelected;
        Map.Dark = _view.IsDarkTheme;
        MapLabel = MapRoot is null ? string.Empty
            : MapSelected is null || ReferenceEquals(MapSelected, MapRoot)
                ? $"{MapRoot.FullPath} · {FormatSize(MapRoot.Size)}"
                : $"{MapSelected.FullPath} · {FormatSize(MapSelected.Size)} · {(MapSelected.Percent * 100).ToString("0.#", _l.CurrentCulture)} %";
        MapVersion++;
        UpEnabled = MapRoot?.Parent is not null;
    }

    /// <summary>Toque en el mapa: elige el rectangulo de ese punto.</summary>
    public void MapTapped(PointF point)
    {
        MapSelected = Map.HitTest(point);
        RedrawMap();
        UpdateActions();
    }

    /// <summary>Doble toque: entra en la carpeta de ese punto (si tiene subcarpetas).</summary>
    public void MapDoubleTapped(PointF point)
    {
        var hit = Map.HitTest(point);
        if (hit is null || !hit.HasChildren)
            return;
        MapRoot = hit;
        MapSelected = null;
        RedrawMap();
        UpdateActions();
    }

    public void MapUp()
    {
        if (MapRoot?.Parent is null)
            return;
        MapSelected = MapRoot;
        MapRoot = MapRoot.Parent;
        RedrawMap();
        UpdateActions();
    }

    // ============ Seleccion y acciones ============

    /// <summary>
    /// Lo marcado con las casillas en la vista actual. En el arbol, si una carpeta marcada cuelga de
    /// otra tambien marcada, se queda solo la de arriba (la papelera se lleva todo lo de dentro).
    /// </summary>
    public List<string> CheckedPaths()
    {
        switch (Mode)
        {
            case DiskViewMode.Tree:
            {
                if (Root is null)
                    return [];
                var result = new List<string>();
                var stack = new Stack<FolderNode>();
                stack.Push(Root);
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    if (n.IsChecked && !ReferenceEquals(n, Root))
                    {
                        result.Add(n.FullPath);
                        continue;   // lo de dentro va con ella
                    }
                    foreach (var c in n.Children)
                        stack.Push(c);
                }
                return result;
            }
            case DiskViewMode.Largest:
                return (Largest ?? []).Where(r => r.IsChecked).Select(r => r.FullPath).ToList();
            case DiskViewMode.Duplicates:
                return (Duplicates ?? []).SelectMany(g => g.Rows).Where(r => r.IsChecked).Select(r => r.FullPath).ToList();
            default:
                return [];
        }
    }

    /// <summary>Sobre lo que actuan los botones: lo marcado si hay algo marcado; si no, lo elegido.</summary>
    public List<string> TargetPaths()
    {
        var checkedPaths = CheckedPaths();
        if (checkedPaths.Count > 0)
            return checkedPaths;
        return SelectedPath() is { } single ? [single] : [];
    }

    public void SelectDuplicate(FileRow row)
    {
        if (_selectedDuplicate is not null)
            _selectedDuplicate.IsSelected = false;
        _selectedDuplicate = row;
        row.IsSelected = true;
        UpdateActions();
    }

    /// <summary>La ruta elegida en la vista actual, o null.</summary>
    public string? SelectedPath() => Mode switch
    {
        DiskViewMode.Tree => (TreeSelected as FolderNode)?.FullPath,
        DiskViewMode.Largest => (LargestSelected as FileRow)?.FullPath,
        DiskViewMode.Duplicates => _selectedDuplicate?.FullPath,
        DiskViewMode.Map => (MapSelected ?? MapRoot)?.FullPath,
        _ => null,
    };

    public void UpdateActions()
    {
        var checkedCount = CheckedPaths().Count;
        var has = SelectedPath() is not null;
        OpenEnabled = has;
        CopyEnabled = has || checkedCount > 0;
        // La raiz escaneada no se manda a la papelera desde aqui.
        DeleteEnabled = checkedCount > 0
                        || (has && !(Mode == DiskViewMode.Tree && ReferenceEquals(TreeSelected, Root))
                                && !(Mode == DiskViewMode.Map && ReferenceEquals(MapSelected ?? MapRoot, Root)));
        ExportEnabled = Root is not null && _scan is null;
        DeleteTooltip = checkedCount > 1 ? string.Format(_l.CurrentCulture, _l["DiskDeleteMany"], checkedCount) : _l["DiskDelete"];
        Notify();
    }

    public async Task OpenAsync()
    {
        if (SelectedPath() is not { } path)
            return;
        try { _shell.RevealInExplorer(path); }
        catch (Exception ex) { await _dialogs.AlertAsync(_l["Error"], ex.Message, _l["Ok"]); }
    }

    public async Task CopyAsync()
    {
        var paths = TargetPaths();
        if (paths.Count == 0)
            return;
        await _view.SetClipboardTextAsync(string.Join(Environment.NewLine, paths));
        _toast.Show(paths.Count > 1 ? string.Format(_l.CurrentCulture, _l["DiskCopiedMany"], paths.Count) : _l["DiskCopied"]);
    }

    /// <summary>Texto del aviso antes de borrar lo que hay en <paramref name="paths"/>.</summary>
    public string DeleteMessage(IReadOnlyList<string> paths, int inBinCount)
    {
        var culture = _l.CurrentCulture;
        var allInBin = inBinCount == paths.Count;
        string message;
        if (paths.Count == 1)
        {
            var isFolder = Directory.Exists(paths[0]);
            message = allInBin
                ? string.Format(culture, _l[isFolder ? "DiskDeleteForeverFolderConfirm" : "DiskDeleteForeverFileConfirm"], Label(paths[0]))
                : string.Format(culture, _l[isFolder ? "DiskDeleteFolderConfirm" : "DiskDeleteFileConfirm"], Label(paths[0]));
        }
        else
        {
            // Varios: cuantos son, cuanto ocupan y los primeros, para que se vea que es lo que va.
            var total = paths.Sum(SizeOf);
            var shown = string.Join(Environment.NewLine, paths.Take(8).Select(Label));
            if (paths.Count > 8)
                shown += Environment.NewLine + string.Format(culture, _l["DiskAndMore"], paths.Count - 8);
            message = string.Format(culture, _l[allInBin ? "DiskDeleteForeverManyConfirm" : "DiskDeleteManyConfirm"], paths.Count, FormatSize(total), shown);
        }
        // Mezcla: unos a la papelera y otros, los que ya estaban dentro, sin vuelta atras.
        if (inBinCount > 0 && !allInBin)
            message += Environment.NewLine + Environment.NewLine + string.Format(culture, _l["DiskDeleteSomeForever"], inBinCount);
        return message;
    }

    public async Task DeleteAsync()
    {
        var paths = TargetPaths();
        if (paths.Count == 0)
            return;
        var culture = _l.CurrentCulture;
        // Lo que ya esta en la papelera no se puede mandar a la papelera: se borra definitivamente.
        var inBin = paths.Count(_shell.IsInRecycleBin);
        var allInBin = inBin == paths.Count;
        var action = allInBin ? _l["DiskDeleteForever"] : _l["DiskDelete"];
        var message = DeleteMessage(paths, inBin);
        // Carpetas del sistema (Windows, Archivos de programa, el perfil…): se avisa del riesgo antes de nada.
        var risky = paths.Select(p => (Path: p, Key: _shell.SystemRisk(p))).Where(r => r.Key is not null).ToList();
        if (risky.Count > 0)
        {
            var lines = string.Join(Environment.NewLine, risky.Take(6).Select(r => "• " + r.Path + " — " + _l[r.Key!]));
            if (risky.Count > 6)
                lines += Environment.NewLine + string.Format(culture, _l["DiskAndMore"], risky.Count - 6);
            // El boton principal es Cancelar: seguir es la opcion peligrosa.
            if (await _dialogs.AlertAsync(_l["DiskRiskTitle"], string.Format(culture, _l["DiskRiskBody"], lines), _l["Cancel"], _l["DiskRiskContinue"]))
                return;
        }
        if (!await _dialogs.AlertAsync(action, message, action, _l["Cancel"]))
            return;
        // Carpetas en las que el escaneo ni pudo entrar: sin permisos seguro; se arreglan antes de intentarlo.
        var inaccessible = paths.Where(p => Root is not null && FindNode(Root, p) is { Inaccessible: true }).ToList();
        if (inaccessible.Count > 0 && !await OfferPermissionsAsync(inaccessible))
            return;
        var failed = await RecycleAsync(paths, allInBin);
        if (failed.Count > 0 && await OfferPermissionsAsync(failed))
            failed = await RecycleAsync(failed, allInBin);
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
            await _dialogs.AlertAsync(_l["Error"], detail, _l["Ok"]);
        }
        if (done.Count > 0)
            _toast.Show(done.Count > 1
                ? string.Format(culture, _l[allInBin ? "DiskDeletedForeverMany" : "DiskDeletedMany"], done.Count)
                : _l[allInBin ? "DiskDeletedForever" : "DiskDeleted"]);
    }

    /// <summary>A la papelera en segundo plano y con el aviso a la vista. Devuelve los que no se fueron.</summary>
    private async Task<IReadOnlyList<string>> RecycleAsync(IReadOnlyList<string> paths, bool forever)
    {
        var culture = _l.CurrentCulture;
        Busy(true, paths.Count == 1
            ? string.Format(culture, _l[forever ? "DiskDeletingForever" : "DiskDeleting"], paths[0])
            : string.Format(culture, _l[forever ? "DiskDeletingForeverMany" : "DiskDeletingMany"], paths.Count));
        try { return await Task.Run(() => _shell.MoveToRecycleBin(paths)); }
        finally { Busy(false, string.Empty); }
    }

    /// <summary>
    /// No se pudo borrar (o ni entrar): se propone hacerse dueño de la carpeta y darse control total,
    /// con permiso de administrador (UAC). True si se hizo.
    /// </summary>
    private async Task<bool> OfferPermissionsAsync(IReadOnlyList<string> paths)
    {
        var culture = _l.CurrentCulture;
        var shown = string.Join(Environment.NewLine, paths.Take(6));
        if (paths.Count > 6)
            shown += Environment.NewLine + string.Format(culture, _l["DiskAndMore"], paths.Count - 6);
        if (!await _dialogs.AlertAsync(_l["DiskPermissionsTitle"], string.Format(culture, _l["DiskPermissionsBody"], shown), _l["DiskPermissionsFix"], _l["Cancel"]))
            return false;
        Busy(true, _l["DiskPermissionsWorking"]);
        bool ok;
        try { ok = await _shell.FixPermissionsAsync(paths); }
        finally { Busy(false, string.Empty); }
        if (!ok)
            _toast.Show(_l["DiskPermissionsDenied"]);
        return ok;
    }

    /// <summary>
    /// Como nombrar una ruta en un aviso: dentro de la papelera la ruta de verdad es «$RA1B2C3», que no
    /// dice nada, asi que delante va el nombre que tenia.
    /// </summary>
    public string Label(string path)
    {
        var display = _shell.IsRecycleBinFolder(path) ? null : _shell.DisplayName(path);
        return display is { Length: > 0 } && !string.Equals(display, Path.GetFileName(path), StringComparison.Ordinal)
            ? display + " — " + path
            : path;
    }

    /// <summary>Lo que ocupa una ruta segun el arbol escaneado (carpeta o fichero); 0 si no esta.</summary>
    public long SizeOf(string path)
    {
        if (Root is null)
            return 0;
        if (FindNode(Root, path) is { } node)
            return node.Size;
        var owner = FindNode(Root, Path.GetDirectoryName(path) ?? string.Empty);
        return owner?.Files.FirstOrDefault(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase))?.Size ?? 0;
    }

    private void Busy(bool on, string text)
    {
        BusyText = text;
        BusyVisible = on;
        ScanEnabled = !on;
        DeleteEnabled = !on && DeleteEnabled;
        Notify();
    }

    /// <summary>Tras borrar: se quita del arbol y se restan los tamanos hacia arriba, sin volver a escanear.</summary>
    public void RemoveFromModel(string path, bool refresh = true)
    {
        if (Root is null)
            return;
        var node = FindNode(Root, path);
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
            var owner = FindNode(Root, Path.GetDirectoryName(path) ?? string.Empty);
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

    /// <summary>Vuelve a pintar las vistas con el arbol ya recortado.</summary>
    private void RefreshAfterRemove()
    {
        if (Root is null)
            return;
        Describe(Root);
        RebuildRows();
        Largest = null;
        Duplicates = null;
        _selectedDuplicate = null;
        if (MapRoot is not null && FindNode(Root, MapRoot.FullPath) is null)
            MapRoot = Root;
        MapSelected = null;
        ShowView(Mode == DiskViewMode.Duplicates ? DiskViewMode.Tree : Mode);
    }

    public static FolderNode? FindNode(FolderNode root, string path)
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

    // ============ Exportar ============

    /// <summary>CSV de la vista actual (separado por punto y coma, como lo abre Excel en castellano).</summary>
    public string BuildCsv()
    {
        var sb = new StringBuilder();
        switch (Mode)
        {
            case DiskViewMode.Largest:
                sb.AppendLine("Ruta;Bytes;Modificado");
                foreach (var f in Root!.AllFiles().OrderByDescending(f => f.Size).Take(300))
                    sb.AppendLine($"{Csv(f.FullPath)};{f.Size};{f.Modified:yyyy-MM-dd HH:mm}");
                break;
            case DiskViewMode.Types:
            case DiskViewMode.Age:
                sb.AppendLine("Grupo;Bytes;Ficheros");
                foreach (var r in Aggregate ?? [])
                    sb.AppendLine($"{Csv(r.Label)};{r.Size};{r.Count}");
                break;
            case DiskViewMode.Duplicates:
                sb.AppendLine("Grupo;Ruta;Bytes;Modificado");
                var n = 0;
                foreach (var g in Duplicates ?? [])
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
                stack.Push(Root!);
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
        return sb.ToString();
    }

    /// <summary>Guarda el CSV en <paramref name="folder"/> y lo enseña en el Explorador.</summary>
    public async Task ExportAsync(string folder, DateTime now)
    {
        if (Root is null)
            return;
        var csv = BuildCsv();
        var file = Path.Combine(folder, $"sOCUninstaller-espacio-{now:yyyyMMdd-HHmmss}.csv");
        Busy(true, string.Format(_l.CurrentCulture, _l["DiskExporting"], file));
        try
        {
            await File.WriteAllTextAsync(file, csv, new UTF8Encoding(true));
            Busy(false, string.Empty);
            _toast.Show(string.Format(_l.CurrentCulture, _l["DiskExported"], file));
            _shell.RevealInExplorer(file);
        }
        catch (Exception ex)
        {
            Busy(false, string.Empty);
            await _dialogs.AlertAsync(_l["Error"], ex.Message, _l["Ok"]);
        }
    }

    public static string Csv(string s) => s.Contains(';') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
