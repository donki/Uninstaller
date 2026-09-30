using System.Globalization;
using Uninstaller.Models;
using Uninstaller.Services;

namespace Uninstaller.Tests;

/// <summary>Arbol de carpetas de mentira en una carpeta temporal (nunca datos reales).</summary>
public sealed class TempTree : IDisposable
{
    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "uninst-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string File(string relative, int bytes, DateTime? modified = null, byte fill = 1)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = new byte[bytes];
        Array.Fill(data, fill);
        System.IO.File.WriteAllBytes(path, data);
        if (modified is { } when)
            System.IO.File.SetLastWriteTime(path, when);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

public sealed class DiskScannerTests : IDisposable
{
    private readonly TempTree _tree = new();

    public void Dispose() => _tree.Dispose();

    [Fact]
    public async Task Suma_tamanos_cuenta_y_ordena_por_tamano()
    {
        var old = new DateTime(2020, 1, 1);
        var recent = new DateTime(2025, 6, 1);
        _tree.File("raiz.txt", 10, old);
        _tree.File("pequena\\a.bin", 100, old);
        _tree.File("grande\\b.bin", 5000, recent);
        _tree.File("grande\\sub\\c.bin", 3000, old);
        _tree.File("grande\\sub\\d.bin", 7, old);
        Directory.CreateDirectory(Path.Combine(_tree.Root, "vacia"));

        FolderNode? early = null;
        var reports = new List<ScanProgress>();
        var root = await DiskScanner.ScanAsync(_tree.Root, new SyncProgress<ScanProgress>(reports.Add), CancellationToken.None, r => early = r);

        Assert.Same(root, early);
        Assert.Equal(8117, root.Size);
        Assert.Equal(5, root.FileCount);
        Assert.Equal(4, root.FolderCount);   // pequena, grande, sub, vacia
        Assert.Equal(recent, root.LastModified);
        Assert.True(root.IsExpanded);
        Assert.Equal(["grande", "pequena", "vacia"], root.Children.Select(c => c.Name));

        var grande = root.Children[0];
        Assert.Equal(8007, grande.Size);
        Assert.Equal(3, grande.FileCount);
        Assert.Equal(1, grande.FolderCount);
        Assert.Equal(1, grande.Depth);
        Assert.Same(root, grande.Parent);
        Assert.Equal(3007, grande.Children[0].Size);
        Assert.Equal(0, root.Children[2].Size);
        Assert.Equal(DateTime.MinValue, root.Children[2].LastModified);

        Assert.Single(root.Files);
        Assert.Equal(5, root.AllFiles().Count());
        Assert.NotEmpty(reports);
        var last = reports[^1];
        Assert.Equal((5L, 5L, 8117L), (last.Files, last.Folders, last.Bytes));
    }

    [Fact]
    public async Task Ruta_que_no_existe()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            DiskScanner.ScanAsync(Path.Combine(_tree.Root, "no-existe"), null, CancellationToken.None));
    }

    [Fact]
    public async Task Cancelar()
    {
        _tree.File("a\\x.bin", 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DiskScanner.ScanAsync(_tree.Root, null, cts.Token));
    }

    [Fact]
    public async Task No_sigue_enlaces_ni_uniones()
    {
        _tree.File("real\\a.bin", 50);
        var link = Path.Combine(_tree.Root, "enlace");
        try
        {
            Directory.CreateSymbolicLink(link, Path.Combine(_tree.Root, "real"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;   // sin permiso para crear enlaces (Windows sin modo desarrollador): no se puede comprobar
        }

        var root = await DiskScanner.ScanAsync(_tree.Root, null, CancellationToken.None);

        Assert.Equal(50, root.Size);
        Assert.Equal(["real"], root.Children.Select(c => c.Name));
    }

    [Fact]
    public async Task Duplicados_por_tamano_y_contenido()
    {
        // Pequeños (caben en el hash parcial) y grandes (hacen falta el hash entero).
        var a1 = _tree.File("a1.bin", 2000, fill: 7);
        var a2 = _tree.File("dir\\a2.bin", 2000, fill: 7);
        var a3 = _tree.File("dir\\a3.bin", 2000, fill: 8);        // mismo tamaño, distinto contenido
        var big1 = _tree.File("big1.bin", 100_000, fill: 3);
        var big2 = _tree.File("big2.bin", 100_000, fill: 3);
        var bigOtherTail = _tree.File("big3.bin", 100_000, fill: 3);
        using (var s = new FileStream(bigOtherTail, FileMode.Open))
        {
            s.Seek(-1, SeekOrigin.End);
            s.WriteByte(9);   // igual al principio, distinto al final
        }
        _tree.File("solo.bin", 999);
        _tree.File("tiny1.bin", 5, fill: 1);
        _tree.File("tiny2.bin", 5, fill: 1);

        var root = await DiskScanner.ScanAsync(_tree.Root, null, CancellationToken.None);
        var reports = new List<ScanProgress>();
        var groups = await DiskScanner.FindDuplicatesAsync(root.AllFiles(), minSize: 10, new SyncProgress<ScanProgress>(reports.Add), CancellationToken.None);

        Assert.Equal(2, groups.Count);
        Assert.Equal(100_000, groups[0].Size);                 // el que mas desperdicia, primero
        Assert.Equal(100_000, groups[0].Wasted);
        Assert.Equal([big1, big2], groups[0].Files.Select(f => f.FullPath).Order());
        Assert.Equal([a1, a2], groups[1].Files.Select(f => f.FullPath).Order());
        Assert.Equal(2000, groups[1].Wasted);
    }

    [Fact]
    public async Task Duplicados_que_no_se_pueden_leer_se_saltan()
    {
        var f1 = _tree.File("x1.bin", 100, fill: 1);
        var f2 = _tree.File("x2.bin", 100, fill: 1);
        var root = await DiskScanner.ScanAsync(_tree.Root, null, CancellationToken.None);
        File.Delete(f2);   // desaparece entre el escaneo y la comparacion

        Assert.Empty(await DiskScanner.FindDuplicatesAsync(root.AllFiles(), 1, null, CancellationToken.None));
        Assert.True(File.Exists(f1));
    }

    [Fact]
    public async Task Duplicados_grandes_que_desaparecen_a_medias()
    {
        var f1 = _tree.File("g1.bin", 70_000, fill: 2);
        var f2 = _tree.File("g2.bin", 70_000, fill: 2);
        var files = new List<ScannedFile>
        {
            new(f1, 70_000, DateTime.Now), new(f2, 70_000, DateTime.Now), new(Path.Combine(_tree.Root, "fantasma.bin"), 70_000, DateTime.Now),
        };
        var groups = await DiskScanner.FindDuplicatesAsync(files, 1, null, CancellationToken.None);
        Assert.Equal(2, Assert.Single(groups).Files.Count);
    }

    [Fact]
    public async Task Muchos_candidatos_informan_del_avance()
    {
        for (var i = 0; i < 60; i++)
            _tree.File($"n{i}.bin", 20, fill: (byte)i);
        var root = await DiskScanner.ScanAsync(_tree.Root, null, CancellationToken.None);
        var reports = new List<ScanProgress>();

        var groups = await DiskScanner.FindDuplicatesAsync(root.AllFiles(), 1, new SyncProgress<ScanProgress>(reports.Add), CancellationToken.None);

        Assert.Empty(groups);
        Assert.Contains(reports, r => r.Folders == 60);
    }

    /// <summary>IProgress que informa en el acto (Progress&lt;T&gt; lo haria despues, en otro hilo).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            lock (this)
                report(value);
        }
    }
}

public class FolderNodeTests
{
    private static FolderNode Tree(out FolderNode child, out FolderNode grandchild)
    {
        var root = new FolderNode { Name = "C:\\", FullPath = "C:\\" };
        child = new FolderNode { Name = "a", FullPath = "C:\\a", Parent = root, Depth = 1 };
        grandchild = new FolderNode { Name = "b", FullPath = "C:\\a\\b", Parent = child, Depth = 2 };
        root.Children = [child];
        child.Children = [grandchild];
        return root;
    }

    [Fact]
    public void Accumulate_sube_hasta_la_raiz()
    {
        var root = Tree(out var child, out var grandchild);
        var when = new DateTime(2024, 1, 1);

        grandchild.Accumulate(100, 2, 1, when);
        child.Accumulate(50, 1, 0, when.AddDays(-1));

        Assert.Equal((150L, 3, 1), (root.Size, root.FileCount, root.FolderCount));
        Assert.Equal((100L, 2, 1), (grandchild.Size, grandchild.FileCount, grandchild.FolderCount));
        Assert.Equal(when, root.LastModified);
        Assert.Equal(1, child.Percent);          // lo tiene todo la unica hija
        Assert.Equal(1, root.Percent);
        Assert.Equal(2d / 3, grandchild.Percent, 3);
    }

    [Fact]
    public void Percent_con_padre_vacio_es_uno()
    {
        Tree(out var child, out _);
        Assert.Equal(1, child.Percent);
    }

    [Fact]
    public void Desplegable_y_notificaciones()
    {
        var root = Tree(out var child, out var grandchild);
        var changes = new List<string?>();
        root.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.True(root.HasChildren);
        Assert.Equal("▸", root.Expander);
        root.IsExpanded = true;
        root.IsExpanded = true;
        Assert.Equal("▾", root.Expander);
        Assert.Equal(string.Empty, grandchild.Expander);
        root.NotifyChildrenChanged();
        root.IsChecked = true;
        root.IsChecked = true;
        root.Display = "Disco local";
        root.Display = "Disco local";
        root.SizeText = "1 GB";
        root.DetailText = "x";
        var icon = ImageSource.FromFile("a.png");
        root.Icon = icon;
        root.Icon = icon;

        Assert.Equal(
            ["IsExpanded", "Expander", "Expander", "IsChecked", "Display", "SizeText", "DetailText", "Icon"],
            changes);
        Assert.True(root.IsChecked);
        Assert.Equal("Disco local", root.Display);
        Assert.Equal("b", grandchild.Display);
        Assert.Same(icon, root.Icon);
        Assert.Equal(new Thickness(36, 0, 0, 0), grandchild.Indent);
        Assert.False(child.Inaccessible);
        Assert.False(child.DisplayResolved);
    }

    [Fact]
    public void AllFiles_recorre_todo()
    {
        var root = Tree(out var child, out var grandchild);
        root.Files.Add(new ScannedFile("C:\\r.txt", 1, DateTime.Now));
        grandchild.Files.Add(new ScannedFile("C:\\a\\b\\g.TXT", 2, DateTime.Now));

        Assert.Equal(["C:\\a\\b\\g.TXT", "C:\\r.txt"], root.AllFiles().Select(f => f.FullPath).Order());
        Assert.Empty(child.Files);
    }

    [Fact]
    public void ScannedFile_partes_de_la_ruta()
    {
        var f = new ScannedFile("C:\\Datos\\Foto.JPG", 1, DateTime.Now);
        Assert.Equal(("Foto.JPG", ".jpg", "C:\\Datos"), (f.Name, f.Extension, f.Folder));
        Assert.Equal(string.Empty, new ScannedFile("C:\\", 0, DateTime.Now).Folder);
    }

    [Fact]
    public void FileRow_y_grupo_de_duplicados()
    {
        var file = new ScannedFile("C:\\d\\a.bin", 10, DateTime.Now);
        var row = new FileRow(file, "10 B", "hoy", 0.5);
        var changes = new List<string?>();
        row.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.Equal(("a.bin", "C:\\d\\a.bin", "C:\\d", "10 B", "hoy", 0.5), (row.Name, row.FullPath, row.Folder, row.SizeText, row.DetailText, row.Percent));
        Assert.Equal("a.bin", row.Display);
        row.Display = "factura.pdf";
        Assert.Equal("factura.pdf", row.Display);
        Assert.Equal(Colors.Transparent, row.RowColor);
        row.IsSelected = true;
        row.IsSelected = true;
        Assert.NotEqual(Colors.Transparent, row.RowColor);
        row.IsChecked = true;
        row.IsChecked = true;
        var icon = ImageSource.FromFile("x.png");
        row.Icon = icon;
        row.Icon = icon;
        Assert.Same(icon, row.Icon);
        Assert.True(row.IsChecked && row.IsSelected);
        Assert.Equal(["IsSelected", "RowColor", "IsChecked", "Icon"], changes);

        var group = new DuplicateGroup { Size = 10, Files = [file, file, file] };
        Assert.Equal(20, group.Wasted);
        group.Title = "t";
        group.Detail = "d";
        Assert.Empty(group.Rows);
        Assert.Equal(("t", "d"), (group.Title, group.Detail));

        var aggregate = new AggregateRow(".jpg", 5, 1, 0.1, "5 B", "10 %");
        Assert.Equal(".jpg", aggregate.Label);
    }
}

public class ListRulesTests
{
    private static InstalledApp App(string label, string package = "p", string publisher = "", long size = 0,
        DateTime? installed = null, DateTime? updated = null) =>
        new()
        {
            PackageName = package, Label = label, Publisher = publisher, SizeBytes = size,
            InstallDate = installed ?? DateTime.MinValue, UpdatedDate = updated ?? DateTime.MinValue,
        };

    private static readonly List<InstalledApp> Apps =
    [
        App("beta", "com.b", size: 10, installed: new(2024, 1, 1), updated: new(2025, 1, 1)),
        App("Alfa", "com.a", size: 30, installed: new(2023, 1, 1), updated: new(2023, 1, 1)),
        App("gamma", "win32:x", publisher: "Contoso", size: 20, installed: new(2025, 1, 1), updated: new(2024, 1, 1)),
    ];

    [Theory]
    [InlineData("name", "Alfa,beta,gamma")]
    [InlineData("updated", "beta,gamma,Alfa")]
    [InlineData("size", "Alfa,gamma,beta")]
    [InlineData("install", "gamma,beta,Alfa")]
    [InlineData(null, "gamma,beta,Alfa")]
    [InlineData("desconocido", "gamma,beta,Alfa")]
    public void Sort(string? mode, string expected) =>
        Assert.Equal(expected, string.Join(',', ListRules.Sort(Apps, mode).Select(a => a.Label)));

    [Theory]
    [InlineData(null, "beta,Alfa,gamma")]
    [InlineData("   ", "beta,Alfa,gamma")]
    [InlineData("ALFA", "Alfa")]
    [InlineData("com.b", "beta")]         // por paquete (Android)
    [InlineData("contoso", "gamma")]      // por editor (Windows)
    [InlineData(" a ", "beta,Alfa,gamma")]
    [InlineData("zzz", "")]
    public void Filter(string? search, string expected) =>
        Assert.Equal(expected, string.Join(',', ListRules.Filter(Apps, search).Select(a => a.Label)));

    [Fact]
    public void Filter_sin_texto_devuelve_la_misma_lista() => Assert.Same(Apps, ListRules.Filter(Apps, ""));

    [Theory]
    [InlineData(0, "—")]
    [InlineData(-5, "—")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1,5 kB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(5L * 1024 * 1024 * 1024 + 1024L * 1024 * 1024 / 2, "5,5 GB")]
    public void AppSize(long bytes, string expected) =>
        Assert.Equal(expected, ListRules.AppSize(bytes, CultureInfo.GetCultureInfo("es-ES")));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 kB")]
    [InlineData(3L * 1024 * 1024 * 1024 + 10L * 1024 * 1024, "3.01 GB")]
    [InlineData(2L * 1024 * 1024 * 1024 * 1024, "2 TB")]
    [InlineData(1024 * 1024 * 25 / 10, "2.5 MB")]
    public void DiskSize(long bytes, string expected) =>
        Assert.Equal(expected, ListRules.DiskSize(bytes, CultureInfo.InvariantCulture));

    [Fact]
    public void CompactDatePattern_deja_el_ano_en_dos_cifras()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
        Assert.Equal("dd/MM/yy", ListRules.CompactDatePattern(culture));

        culture.DateTimeFormat.ShortDatePattern = "yyyy-MM-dd";
        Assert.Equal("yy-MM-dd", ListRules.CompactDatePattern(culture));

        culture.DateTimeFormat.ShortDatePattern = "d.M.yy";
        Assert.Equal("d.M.yy", ListRules.CompactDatePattern(culture));

        Assert.DoesNotContain("yyyy", ListRules.CompactDatePattern(CultureInfo.GetCultureInfo("es-ES")));
    }

    [Fact]
    public void Flatten_solo_baja_por_lo_desplegado()
    {
        var root = new FolderNode { Name = "r", FullPath = "r", IsExpanded = true };
        var a = new FolderNode { Name = "a", FullPath = "r\\a", Parent = root, Depth = 1, IsExpanded = true };
        var b = new FolderNode { Name = "b", FullPath = "r\\b", Parent = root, Depth = 1 };
        a.Children = [new FolderNode { Name = "a1", FullPath = "r\\a\\a1", Parent = a, Depth = 2 }];
        b.Children = [new FolderNode { Name = "b1", FullPath = "r\\b\\b1", Parent = b, Depth = 2 }];
        root.Children = [a, b];

        var rows = new List<FolderNode>();
        ListRules.Flatten(root, rows);

        Assert.Equal(["r", "a", "a1", "b"], rows.Select(n => n.Name));
    }

    [Fact]
    public void Largest_con_su_parte_y_sin_dividir_por_cero()
    {
        var files = new[] { new ScannedFile("a", 50, DateTime.Now), new ScannedFile("b", 200, DateTime.Now), new ScannedFile("c", 100, DateTime.Now) };
        var top = ListRules.Largest(files, 2);
        Assert.Equal([("b", 1.0), ("c", 0.5)], top.Select(t => (t.File.FullPath, t.Percent)));

        // Fallo arreglado: con todos vacios salia 0/0 = NaN y la barra no se podia pintar.
        var empty = ListRules.Largest([new ScannedFile("z", 0, DateTime.Now)], 5);
        Assert.Equal(0, Assert.Single(empty).Percent);
        Assert.Empty(ListRules.Largest([], 5));
    }

    [Fact]
    public void ByType_agrupa_por_extension()
    {
        var files = new[]
        {
            new ScannedFile("C:\\a.JPG", 10, DateTime.Now), new ScannedFile("C:\\b.jpg", 5, DateTime.Now),
            new ScannedFile("C:\\c.txt", 1, DateTime.Now), new ScannedFile("C:\\LEEME", 30, DateTime.Now),
        };
        var types = ListRules.ByType(files, "(sin extension)", 10);
        Assert.Equal([("(sin extension)", 30L, 1), (".jpg", 15L, 2), (".txt", 1L, 1)], types);
        Assert.Single(ListRules.ByType(files, "-", 1));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(29.9, 0)]
    [InlineData(30, 1)]
    [InlineData(179, 1)]
    [InlineData(180, 2)]
    [InlineData(364, 2)]
    [InlineData(365, 3)]
    [InlineData(729, 3)]
    [InlineData(730, 4)]
    [InlineData(5000, 4)]
    [InlineData(-3, 0)]   // fecha en el futuro (reloj de otro equipo): cuenta como reciente
    public void AgeBucket(double days, int expected) => Assert.Equal(expected, ListRules.AgeBucket(TimeSpan.FromDays(days)));

    [Fact]
    public void ByAge_suma_por_tramos()
    {
        var now = new DateTime(2026, 9, 30);
        var files = new[]
        {
            new ScannedFile("a", 1, now.AddDays(-1)), new ScannedFile("b", 2, now.AddDays(-2)),
            new ScannedFile("c", 4, now.AddDays(-100)), new ScannedFile("d", 8, now.AddYears(-5)),
        };
        var (sizes, counts) = ListRules.ByAge(files, now);
        Assert.Equal([3L, 4L, 0L, 0L, 8L], sizes);
        Assert.Equal([2, 1, 0, 0, 1], counts);
    }
}

public class InstalledAppTests
{
    [Fact]
    public void Segunda_linea_y_avisos()
    {
        var android = new InstalledApp { PackageName = "com.whatsapp", Label = "WhatsApp" };
        var windows = new InstalledApp { PackageName = "win32:{G}", Label = "7-Zip", Publisher = "Igor Pavlov", SupportsUnattended = true };
        Assert.Equal("com.whatsapp", android.Subtitle);
        Assert.Equal("Igor Pavlov", windows.Subtitle);
        Assert.True(windows.SupportsUnattended);
        Assert.False(android.IsSystem);
        Assert.Null(android.Icon);

        var changes = new List<string?>();
        android.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        android.Details = "x";
        android.Details = "x";
        android.IsSelected = true;
        android.IsSelected = true;
        android.SizeBytes = 5;
        Assert.Equal(["Details", "IsSelected"], changes);
        Assert.Equal(("x", true, 5L), (android.Details, android.IsSelected, android.SizeBytes));
    }
}
