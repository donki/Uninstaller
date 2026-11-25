using Uninstaller.Services;

namespace Uninstaller.Tests;

/// <summary>Borrado definitivo fichero a fichero: lo que no se deja borrar, solo lectura, el aviso final y parar.</summary>
public sealed class FileDeleterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uninst-del-").FullName;

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFileSystemEntries(_dir, "*", SearchOption.AllDirectories))
            try { File.SetAttributes(f, File.GetAttributes(f) & ~FileAttributes.ReadOnly); } catch (IOException) { }
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Write(string relative, int bytes = 10)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private sealed class SyncProgress(Action<DeleteProgress> report) : IProgress<DeleteProgress>
    {
        public void Report(DeleteProgress value) => report(value);
    }

    [Fact]
    public void Un_fichero_abierto_se_queda_y_con_el_su_carpeta()
    {
        var folder = Path.Combine(_dir, "Abierta");
        var open = Write(@"Abierta\en-uso.txt");
        var other = Write(@"Abierta\libre.txt");
        var loose = Write("suelto-en-uso.txt");
        using (new FileStream(open, FileMode.Open, FileAccess.Read, FileShare.None))
        using (new FileStream(loose, FileMode.Open, FileAccess.Read, FileShare.None))
            FileDeleter.Delete([folder, loose], null, CancellationToken.None);

        Assert.True(File.Exists(open));
        Assert.True(File.Exists(loose));
        Assert.False(File.Exists(other));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void Solo_lectura_y_carpeta_de_solo_lectura_se_borran()
    {
        var folder = Path.Combine(_dir, "Protegida");
        var file = Write(@"Protegida\Dentro\leer.txt");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        File.SetAttributes(Path.Combine(folder, "Dentro"), FileAttributes.Directory | FileAttributes.ReadOnly);
        File.SetAttributes(folder, FileAttributes.Directory | FileAttributes.ReadOnly);

        FileDeleter.Delete([folder], null, CancellationToken.None);

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Parado_antes_de_empezar_no_borra_nada()
    {
        var folder = Path.Combine(_dir, "Intacta");
        var file = Write(Path.Combine("Intacta", "a.txt"));
        var keep = Write("intacto.txt");

        FileDeleter.Delete([folder, keep], null, new CancellationToken(canceled: true));

        Assert.True(File.Exists(file));
        Assert.True(File.Exists(keep));
    }

    [Fact]
    public void Lo_que_no_existe_no_cuenta_y_el_final_se_avisa()
    {
        var reports = new List<DeleteProgress>();
        FileDeleter.Delete([Path.Combine(_dir, "no-esta"), Write("a.txt")], new SyncProgress(reports.Add), CancellationToken.None);
        Assert.Equal(new DeleteProgress(1, string.Empty), reports[^1]);
    }
}
