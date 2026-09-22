using System.ComponentModel;

namespace Uninstaller.Models;

/// <summary>Un fichero visto por el escaner de espacio: lo justo para listar, agrupar y buscar duplicados.</summary>
public sealed record ScannedFile(string FullPath, long Size, DateTime Modified)
{
    public string Name => Path.GetFileName(FullPath);
    public string Extension => Path.GetExtension(FullPath).ToLowerInvariant();
    public string Folder => Path.GetDirectoryName(FullPath) ?? string.Empty;
}

/// <summary>
/// Una carpeta del arbol de espacio en disco (estilo TreeSize): tamaño acumulado, cuantos ficheros
/// y carpetas cuelgan de ella, sus hijas y los ficheros que tiene directamente. La pagina la pinta
/// en una lista aplanada, de ahi <see cref="Depth"/>, <see cref="IsExpanded"/> y <see cref="Percent"/>.
/// </summary>
public sealed class FolderNode : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public FolderNode? Parent { get; init; }
    public int Depth { get; init; }

    /// <summary>Bytes de todo lo que cuelga (ficheros propios y de todas las subcarpetas).</summary>
    public long Size { get; set; }
    public int FileCount { get; set; }
    public int FolderCount { get; set; }
    public DateTime LastModified { get; set; }

    /// <summary>No se pudo entrar (permisos): el tamaño es parcial.</summary>
    public bool Inaccessible { get; set; }

    public List<FolderNode> Children { get; } = [];
    public List<ScannedFile> Files { get; } = [];

    /// <summary>Parte del padre que ocupa esta carpeta (0..1); en la raiz, 1.</summary>
    public double Percent => Parent is null || Parent.Size <= 0 ? 1 : Math.Min(1, Size / (double)Parent.Size);

    public bool HasChildren => Children.Count > 0;

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Expander)));
        }
    }

    /// <summary>Glifo del desplegable: vacio si no hay subcarpetas.</summary>
    public string Expander => !HasChildren ? string.Empty : IsExpanded ? "▾" : "▸";

    private bool _isChecked;
    /// <summary>Marcada con su casilla para actuar sobre varias a la vez (papelera, copiar rutas).</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    /// <summary>Sangria de la fila segun la profundidad.</summary>
    public Thickness Indent => new(Depth * 18, 0, 0, 0);

    // Textos ya formateados (los compone la pagina, que sabe de idioma y cultura).
    private string _sizeText = string.Empty, _detailText = string.Empty;
    public string SizeText { get => _sizeText; set { _sizeText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText))); } }
    public string DetailText { get => _detailText; set { _detailText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailText))); } }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Todos los ficheros de esta carpeta y de las que cuelgan de ella.</summary>
    public IEnumerable<ScannedFile> AllFiles()
    {
        var stack = new Stack<FolderNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            foreach (var f in node.Files)
                yield return f;
            foreach (var c in node.Children)
                stack.Push(c);
        }
    }
}

/// <summary>Fila de una vista agregada (tipo de fichero, tramo de antigüedad): etiqueta, tamaño y cuenta.</summary>
public sealed record AggregateRow(string Label, long Size, int Count, double Percent, string SizeText, string DetailText);

/// <summary>Un fichero en la lista de los mas grandes o dentro de un grupo de duplicados.</summary>
public sealed class FileRow(ScannedFile file, string sizeText, string detailText, double percent) : INotifyPropertyChanged
{
    public ScannedFile File { get; } = file;
    public string SizeText { get; } = sizeText;
    public string DetailText { get; } = detailText;
    public double Percent { get; } = percent;
    public string Name => File.Name;
    public string FullPath => File.FullPath;
    public string Folder => File.Folder;

    private bool _isSelected;
    /// <summary>Elegido dentro de un grupo de duplicados (la fila se resalta).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowColor)));
        }
    }

    public Color RowColor => IsSelected ? Color.FromArgb("#333525CD") : Colors.Transparent;

    private bool _isChecked;
    /// <summary>Marcado con su casilla para actuar sobre varios a la vez.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Un grupo de ficheros identicos: mismo tamaño y mismo hash.</summary>
public sealed class DuplicateGroup
{
    public required long Size { get; init; }
    public required IReadOnlyList<ScannedFile> Files { get; init; }
    /// <summary>Lo que se recuperaria dejando una sola copia.</summary>
    public long Wasted => Size * (Files.Count - 1);
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public List<FileRow> Rows { get; } = [];
}

/// <summary>Avance del escaneo, para la barra: lo que lleva contado y por donde va.</summary>
public sealed record ScanProgress(long Files, long Folders, long Bytes, string Current);
