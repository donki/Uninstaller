using System.Text;

namespace Uninstaller.Services;

/// <summary>
/// La papelera de Windows vista desde sus rutas y sus fichas «$I…», sin llamar al sistema: donde
/// esta, que es de ella y que nombre tenia cada cosa antes de borrarla.
/// </summary>
public static class RecycleBinPaths
{
    private static string[] Parts(string path) => path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

    private static bool IsBinName(string name) =>
        name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) || name.Equals("RECYCLER", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// La papelera de la unidad (<c>X:\$Recycle.Bin</c>, o <c>RECYCLER</c> en discos viejos) o la
    /// carpeta de un usuario dentro de ella (<c>X:\$Recycle.Bin\S-1-5-…</c>).
    /// </summary>
    public static bool IsBinFolder(string path)
    {
        var parts = Parts(path);
        return parts.Length is 2 or 3 && IsBinName(parts[1]);
    }

    /// <summary>El SID del usuario si la ruta es su carpeta dentro de la papelera; si no, null.</summary>
    public static string? OwnerSid(string path)
    {
        var parts = Parts(path);
        return parts.Length == 3 && IsBinName(parts[1]) ? parts[2] : null;
    }

    /// <summary>
    /// Si la ruta cuelga de la papelera de cualquier unidad. La carpeta en si no cuenta: vaciarla
    /// entera se hace borrando lo de dentro.
    /// </summary>
    public static bool IsInside(string path)
    {
        var parts = Parts(path);
        // El primer tramo es la unidad o el servidor; la papelera es el segundo, y tiene que haber algo dentro.
        return parts.Length > 2 && IsBinName(parts[1]);
    }

    /// <summary>
    /// La ficha «$I…» que acompaña a un contenido «$R…» de la papelera (misma carpeta, mismo resto
    /// del nombre). Null si la ruta no es un «$R…».
    /// </summary>
    public static string? InfoFileFor(string path)
    {
        var name = Path.GetFileName(path);
        if (!name.StartsWith("$R", StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(path) is not { Length: > 0 } dir)
            return null;
        return Path.Combine(dir, "$I" + name[2..]);
    }

    /// <summary>
    /// El nombre original sacado de una ficha «$I…». Formato (Windows Vista en adelante): 8 bytes de
    /// version, 8 el tamaño, 8 la fecha de borrado y, en la version 2, 4 bytes con la longitud del
    /// nombre; despues la ruta original en UTF-16 terminada en nulo. En la version 1 la ruta ocupa
    /// 520 bytes fijos desde el byte 24. Null si la ficha no se entiende.
    /// </summary>
    public static string? OriginalName(byte[] info)
    {
        if (info.Length < 26)
            return null;
        var version = BitConverter.ToInt64(info, 0);
        int start, chars;
        if (version >= 2)
        {
            if (info.Length < 28)
                return null;
            start = 28;
            chars = BitConverter.ToInt32(info, 24);
        }
        else
        {
            start = 24;
            chars = 260;
        }
        if (chars <= 0 || start + ((long)chars * 2) > info.Length)
            chars = (info.Length - start) / 2;
        var full = Encoding.Unicode.GetString(info, start, chars * 2);
        // La ruta acaba en el primer nulo: lo que venga detras (relleno de la version 1) no es nombre.
        var nul = full.IndexOf('\0');
        if (nul >= 0)
            full = full[..nul];
        return full.Length == 0 ? null : Path.GetFileName(full.TrimEnd('\\'));
    }
}
