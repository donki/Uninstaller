using System.Diagnostics;

namespace Uninstaller.Services;

/// <summary>Por donde va un borrado definitivo: ficheros borrados hasta ahora y el que esta en curso.</summary>
public sealed record DeleteProgress(int Files, string Current);

/// <summary>
/// Borrado definitivo, sin papelera, fichero a fichero: asi se puede enseñar por donde va y parar a
/// medias (Directory.Delete recursivo no avisa de nada y no se puede interrumpir). Los enlaces
/// (uniones, enlaces simbolicos) se quitan sin entrar en ellos, para no borrar lo que hay al otro lado.
/// </summary>
public static class FileDeleter
{
    /// <summary>Borra esas rutas (ficheros o carpetas enteras). Lo que no se pudo sigue en el disco.</summary>
    public static void Delete(IReadOnlyList<string> paths, IProgress<DeleteProgress>? progress, CancellationToken token)
    {
        var files = 0;
        var clock = Stopwatch.StartNew();
        void Tick(string current)
        {
            files++;
            if (clock.ElapsedMilliseconds < 150)
                return;
            clock.Restart();
            progress?.Report(new DeleteProgress(files, current));
        }

        foreach (var path in paths)
        {
            if (token.IsCancellationRequested)
                return;
            if (Directory.Exists(path))
                DeleteFolder(path, Tick, token);
            else if (File.Exists(path))
            {
                DeleteFile(path);
                Tick(path);
            }
        }
        progress?.Report(new DeleteProgress(files, string.Empty));
    }

    private static void DeleteFolder(string root, Action<string> tick, CancellationToken token)
    {
        // Recorrido en profundidad sin recursion (hay arboles muy hondos): cada carpeta se visita dos
        // veces, al entrar (se borran sus ficheros y se apilan sus hijas) y al salir (ya vacia, se quita).
        var stack = new Stack<(string Dir, bool Emptied)>();
        stack.Push((root, false));
        while (stack.Count > 0)
        {
            if (token.IsCancellationRequested)
                return;
            var (dir, emptied) = stack.Pop();
            if (emptied)
            {
                DeleteEmptyFolder(dir);
                continue;
            }
            FileAttributes attributes;
            try { attributes = File.GetAttributes(dir); }
            catch (Exception) { continue; }
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                DeleteEmptyFolder(dir);   // la union, no lo que hay detras
                continue;
            }
            stack.Push((dir, true));
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    if (token.IsCancellationRequested)
                        return;
                    DeleteFile(file);
                    tick(file);
                }
                foreach (var sub in Directory.EnumerateDirectories(dir))
                    stack.Push((sub, false));
            }
            catch (Exception)
            {
                // Sin permiso para listarla: se queda, y con ella la carpeta de arriba.
            }
        }
    }

    private static void DeleteFile(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (UnauthorizedAccessException)
        {
            // Solo lectura: se le quita y se reintenta.
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch (Exception) { }
        }
        catch (Exception) { }
    }

    private static void DeleteEmptyFolder(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: false);
        }
        catch (Exception)
        {
            try
            {
                File.SetAttributes(dir, FileAttributes.Directory);
                Directory.Delete(dir, recursive: false);
            }
            catch (Exception) { }
        }
    }
}
