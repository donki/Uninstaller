using System.IO.Compression;
using System.Text;
using Uninstaller.Launcher;

namespace Uninstaller.Tests;

/// <summary>El lanzador de un solo exe: desempaquetar la app sin dejarla nunca a medias y sin acumular versiones.</summary>
public sealed class LauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uninst-launcher-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static MemoryStream Zip(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
                using (var writer = new StreamWriter(archive.CreateEntry(name).Open()))
                    writer.Write(content);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Desempaqueta_marca_completa_y_borra_las_versiones_viejas()
    {
        var old = Directory.CreateDirectory(Path.Combine(_root, "2026.1.1.0")).FullName;
        File.WriteAllText(Path.Combine(old, "Uninstaller.exe"), "viejo");
        var target = Path.Combine(_root, "2026.11.24.0");
        Assert.True(Program.NeedsUnpack(target));

        using var zip = Zip(("Uninstaller.exe", "nuevo"), (@"lib/a.dll", "a"));
        Program.Unpack(zip, _root, target);

        Assert.Equal("nuevo", File.ReadAllText(Path.Combine(target, "Uninstaller.exe")));
        Assert.True(File.Exists(Path.Combine(target, "lib", "a.dll")));
        Assert.True(File.Exists(Path.Combine(target, ".completa")));
        Assert.False(Directory.Exists(old));
        Assert.False(Directory.Exists(target + ".nuevo"));
        Assert.False(Program.NeedsUnpack(target));
    }

    [Fact]
    public void Una_vez_a_medias_se_rehace_entera()
    {
        var target = Path.Combine(_root, "2026.11.24.0");
        // Lo que quedo de un arranque cortado: la carpeta sin la marca y un temporal viejo.
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "Uninstaller.exe"), "cortado");
        File.WriteAllText(Path.Combine(target, "sobra.txt"), "x");
        Directory.CreateDirectory(target + ".nuevo");
        File.WriteAllText(Path.Combine(target + ".nuevo", "basura"), "x");
        Assert.True(Program.NeedsUnpack(target));

        using var zip = Zip(("Uninstaller.exe", "bueno"));
        Program.Unpack(zip, _root, target);

        Assert.Equal("bueno", File.ReadAllText(Path.Combine(target, "Uninstaller.exe")));
        Assert.False(File.Exists(Path.Combine(target, "sobra.txt")));
        Assert.False(Directory.Exists(target + ".nuevo"));
    }

    [Fact]
    public void Leer_un_recurso_y_su_falta()
    {
        Assert.Equal("2026.11.24.0\n", Program.Read(new MemoryStream(Encoding.UTF8.GetBytes("2026.11.24.0\n")), "version.txt"));

        var ex = Assert.Throws<InvalidOperationException>(() => Program.Read(null, "version.txt"));
        Assert.Contains("version.txt", ex.Message);
    }
}
