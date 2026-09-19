using System.Collections.Concurrent;
using System.Security.Cryptography;
using Uninstaller.Models;

namespace Uninstaller.Services;

/// <summary>
/// El escaner de espacio en disco (estilo TreeSize): recorre una unidad, una carpeta o una ruta de
/// red (UNC) y construye el arbol de <see cref="FolderNode"/> con tamaños acumulados. Es .NET puro
/// (System.IO), asi que vale igual para una unidad local, una de red mapeada o \\servidor\recurso.
/// </summary>
/// <remarks>
/// Los enlaces simbolicos y puntos de union no se siguen (se contarian dos veces, y en Windows los
/// hay circulares). Las carpetas sin permiso se marcan <see cref="FolderNode.Inaccessible"/> y se
/// sigue con las demas. Las subcarpetas de los dos primeros niveles se recorren en paralelo: en un
/// disco grande es lo que separa medio minuto de varios.
/// </remarks>
public static class DiskScanner
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public static Task<FolderNode> ScanAsync(string root, IProgress<ScanProgress>? progress, CancellationToken token) => Task.Run(() =>
    {
        var info = new DirectoryInfo(root);
        if (!info.Exists)
            throw new DirectoryNotFoundException(root);
        var counters = new Counters(progress);
        var node = new FolderNode { Name = info.Name.Length > 0 ? info.Name : info.FullName, FullPath = info.FullName, Depth = 0 };
        Walk(node, info, counters, token, parallelLevels: 2);
        counters.Flush(node.FullPath);
        return node;
    }, token);

    private static void Walk(FolderNode node, DirectoryInfo dir, Counters counters, CancellationToken token, int parallelLevels)
    {
        token.ThrowIfCancellationRequested();
        var subdirs = new List<(FolderNode Node, DirectoryInfo Dir)>();
        long size = 0;
        var files = 0;
        var newest = DateTime.MinValue;
        try
        {
            foreach (var entry in dir.EnumerateFileSystemInfos("*", Options))
            {
                token.ThrowIfCancellationRequested();
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                if (entry is DirectoryInfo d)
                {
                    subdirs.Add((new FolderNode { Name = d.Name, FullPath = d.FullName, Parent = node, Depth = node.Depth + 1 }, d));
                }
                else if (entry is FileInfo f)
                {
                    long length;
                    DateTime modified;
                    try { length = f.Length; modified = f.LastWriteTime; }
                    catch (Exception) { continue; }
                    node.Files.Add(new ScannedFile(f.FullName, length, modified));
                    size += length;
                    files++;
                    if (modified > newest)
                        newest = modified;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            node.Inaccessible = true;
        }
        counters.Add(files, 1, size, node.FullPath);

        node.Children.AddRange(subdirs.Select(s => s.Node));
        if (parallelLevels > 0 && subdirs.Count > 1)
        {
            Parallel.ForEach(subdirs, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Environment.ProcessorCount },
                s => Walk(s.Node, s.Dir, counters, token, parallelLevels - 1));
        }
        else
        {
            foreach (var s in subdirs)
                Walk(s.Node, s.Dir, counters, token, 0);
        }

        var folders = 0;
        foreach (var child in node.Children)
        {
            size += child.Size;
            files += child.FileCount;
            folders += child.FolderCount + 1;
            if (child.LastModified > newest)
                newest = child.LastModified;
        }
        node.Size = size;
        node.FileCount = files;
        node.FolderCount = folders;
        node.LastModified = newest;
        node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
    }

    /// <summary>Contadores compartidos entre hilos; avisan del progreso como mucho cinco veces por segundo.</summary>
    private sealed class Counters(IProgress<ScanProgress>? progress)
    {
        private long _files, _folders, _bytes;
        private long _lastTick;

        public void Add(int files, int folders, long bytes, string current)
        {
            Interlocked.Add(ref _files, files);
            Interlocked.Add(ref _folders, folders);
            Interlocked.Add(ref _bytes, bytes);
            var now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastTick) < 200)
                return;
            Interlocked.Exchange(ref _lastTick, now);
            progress?.Report(new ScanProgress(Interlocked.Read(ref _files), Interlocked.Read(ref _folders), Interlocked.Read(ref _bytes), current));
        }

        public void Flush(string current) => progress?.Report(new ScanProgress(_files, _folders, _bytes, current));
    }

    // ------------------------------------------------------------------ duplicados

    /// <summary>
    /// Ficheros identicos: primero se agrupan por tamaño (lo barato), despues por el hash de los
    /// primeros 64 KB y por ultimo, los que aun coinciden, por el hash entero. Solo se leen los
    /// que tienen algun candidato del mismo tamaño.
    /// </summary>
    public static Task<List<DuplicateGroup>> FindDuplicatesAsync(IEnumerable<ScannedFile> files, long minSize, IProgress<ScanProgress>? progress, CancellationToken token) => Task.Run(() =>
    {
        var bySize = files.Where(f => f.Size >= minSize).GroupBy(f => f.Size).Where(g => g.Count() > 1).ToList();
        var candidates = bySize.Sum(g => g.Count());
        long done = 0;
        var result = new ConcurrentBag<DuplicateGroup>();

        Parallel.ForEach(bySize, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, group =>
        {
            var list = group.ToList();
            // Hash parcial
            var byHead = new Dictionary<string, List<ScannedFile>>();
            foreach (var f in list)
            {
                token.ThrowIfCancellationRequested();
                var h = Hash(f.FullPath, 64 * 1024);
                var n = Interlocked.Increment(ref done);
                if (n % 50 == 0)
                    progress?.Report(new ScanProgress(n, candidates, 0, f.FullPath));
                if (h is null)
                    continue;
                if (!byHead.TryGetValue(h, out var bucket))
                    byHead[h] = bucket = [];
                bucket.Add(f);
            }
            foreach (var bucket in byHead.Values.Where(b => b.Count > 1))
            {
                // Los pequeños ya estan enteros en el hash parcial; los grandes se rematan.
                if (group.Key <= 64 * 1024)
                {
                    result.Add(new DuplicateGroup { Size = group.Key, Files = bucket });
                    continue;
                }
                var byFull = new Dictionary<string, List<ScannedFile>>();
                foreach (var f in bucket)
                {
                    token.ThrowIfCancellationRequested();
                    var h = Hash(f.FullPath, long.MaxValue);
                    if (h is null)
                        continue;
                    if (!byFull.TryGetValue(h, out var b2))
                        byFull[h] = b2 = [];
                    b2.Add(f);
                }
                foreach (var b2 in byFull.Values.Where(b => b.Count > 1))
                    result.Add(new DuplicateGroup { Size = group.Key, Files = b2 });
            }
        });

        return result.OrderByDescending(g => g.Wasted).ToList();
    }, token);

    private static string? Hash(string path, long maxBytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            using var sha = SHA256.Create();
            var buffer = new byte[1 << 16];
            long total = 0;
            int read;
            while (total < maxBytes && (read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, maxBytes - total))) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
                total += read;
            }
            sha.TransformFinalBlock([], 0, 0);
            return Convert.ToHexString(sha.Hash!);
        }
        catch (Exception)
        {
            return null;   // en uso o sin permiso: no se puede comparar
        }
    }
}
