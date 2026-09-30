using System.Globalization;
using Uninstaller.Models;

namespace Uninstaller.Services;

/// <summary>
/// Orden, busqueda y formatos de las listas (apps y espacio en disco). Codigo puro: las paginas lo
/// usan para pintar y aqui se prueba.
/// </summary>
public static class ListRules
{
    /// <summary>Apps en el orden elegido: nombre, ultima actualizacion, tamaño o instalacion (por defecto).</summary>
    public static List<InstalledApp> Sort(IEnumerable<InstalledApp> apps, string? mode) => (mode switch
    {
        "name" => apps.OrderBy(a => a.Label, StringComparer.CurrentCultureIgnoreCase),
        "updated" => apps.OrderByDescending(a => a.UpdatedDate),
        "size" => apps.OrderByDescending(a => a.SizeBytes),
        _ => apps.OrderByDescending(a => a.InstallDate),
    }).ToList();

    /// <summary>
    /// Lo que casa con lo escrito, por nombre visible o por la segunda linea (paquete o editor):
    /// quien busca «whatsapp» y quien busca «com.whatsapp» quieren lo mismo. Sin texto, todo.
    /// </summary>
    public static List<InstalledApp> Filter(List<InstalledApp> apps, string? search)
    {
        var term = (search ?? string.Empty).Trim();
        return term.Length == 0
            ? apps
            : apps.Where(app =>
                app.Label.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                app.Subtitle.Contains(term, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    /// <summary>Tamaño de una app: «—» si no se sabe; con un decimal como mucho.</summary>
    public static string AppSize(long bytes, CultureInfo culture)
    {
        const long Kb = 1024, Mb = Kb * 1024, Gb = Mb * 1024;
        return bytes switch
        {
            <= 0 => "—",
            >= Gb => $"{(bytes / (double)Gb).ToString("0.#", culture)} GB",
            >= Mb => $"{(bytes / (double)Mb).ToString("0.#", culture)} MB",
            >= Kb => $"{(bytes / (double)Kb).ToString("0.#", culture)} kB",
            _ => $"{bytes.ToString(culture)} B",
        };
    }

    /// <summary>Tamaño en la vista de disco: «0» si no hay nada; hasta TB, con dos decimales en GB y TB.</summary>
    public static string DiskSize(long bytes, CultureInfo culture)
    {
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

    /// <summary>Fecha corta con el año de dos cifras: la linea entera tiene que caber en la fila.</summary>
    public static string CompactDatePattern(CultureInfo culture) =>
        culture.DateTimeFormat.ShortDatePattern.Replace("yyyy", "yy");

    /// <summary>El arbol como lista: cada carpeta y, si esta desplegada, sus hijas debajo.</summary>
    public static void Flatten(FolderNode node, List<FolderNode> into)
    {
        into.Add(node);
        if (!node.IsExpanded)
            return;
        foreach (var c in node.Children)
            Flatten(c, into);
    }

    /// <summary>
    /// Los ficheros mas grandes, con la parte que ocupa cada uno respecto al mayor (0..1). Si todos
    /// estan vacios la parte es 0: antes salia 0/0 (NaN) y la barra de la fila no se sabia pintar.
    /// </summary>
    public static List<(ScannedFile File, double Percent)> Largest(IEnumerable<ScannedFile> files, int count)
    {
        var top = files.OrderByDescending(f => f.Size).Take(count).ToList();
        var max = top.Count > 0 ? top[0].Size : 0;
        return top.Select(f => (f, max > 0 ? f.Size / (double)max : 0)).ToList();
    }

    /// <summary>Espacio por tipo de fichero (extension), de mayor a menor; los sin extension van juntos.</summary>
    public static List<(string Label, long Size, int Count)> ByType(IEnumerable<ScannedFile> files, string noExtensionLabel, int count) =>
        files
            .GroupBy(f => f.Extension.Length > 0 ? f.Extension : noExtensionLabel)
            .Select(g => (Label: g.Key, Size: g.Sum(f => f.Size), Count: g.Count()))
            .OrderByDescending(g => g.Size)
            .Take(count)
            .ToList();

    /// <summary>Tramos de antigüedad: menos de 30 dias, de 6 meses, de 1 año, de 2 años, y mas.</summary>
    public const int AgeBuckets = 5;

    public static int AgeBucket(TimeSpan age) => age.TotalDays switch
    {
        < 30 => 0,
        < 180 => 1,
        < 365 => 2,
        < 730 => 3,
        _ => 4,
    };

    /// <summary>Bytes y ficheros de cada tramo de antigüedad (por fecha de modificacion).</summary>
    public static (long[] Sizes, int[] Counts) ByAge(IEnumerable<ScannedFile> files, DateTime now)
    {
        var sizes = new long[AgeBuckets];
        var counts = new int[AgeBuckets];
        foreach (var f in files)
        {
            var i = AgeBucket(now - f.Modified);
            sizes[i] += f.Size;
            counts[i]++;
        }
        return (sizes, counts);
    }
}
