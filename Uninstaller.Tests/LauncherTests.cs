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

    /// <summary>Un exe del lanzador hecho a mano: sus recursos salen de un diccionario.</summary>
    private sealed class FakeLauncher(Dictionary<string, Func<Stream>> resources) : System.Reflection.Assembly
    {
        public override Stream? GetManifestResourceStream(string name) => resources.TryGetValue(name, out var open) ? open() : null;
    }

    [Fact]
    public void Arranca_la_version_que_lleva_dentro_con_sus_argumentos()
    {
        var exe = new FakeLauncher(new()
        {
            ["version.txt"] = () => new MemoryStream(Encoding.UTF8.GetBytes("2026.11.25.0\r\n")),
            ["app.zip"] = () => Zip(("Uninstaller.exe", "app")),
        });
        var started = new List<System.Diagnostics.ProcessStartInfo>();
        var errors = new List<string>();

        Assert.Equal(0, Program.Run(["--disk", @"C:\"], exe, _root, started.Add, errors.Add));
        Assert.Equal(0, Program.Run([], exe, _root, started.Add, errors.Add));   // ya desempaquetada: no se rehace

        Assert.Empty(errors);
        var target = Path.Combine(_root, "sOCUninstaller", "app", "2026.11.25.0");
        Assert.Equal(2, started.Count);
        Assert.Equal(Path.Combine(target, "Uninstaller.exe"), started[0].FileName);
        Assert.Equal(target, started[0].WorkingDirectory);
        Assert.False(started[0].UseShellExecute);
        Assert.Equal(["--disk", @"C:\"], started[0].ArgumentList);
        Assert.Equal(Environment.ProcessPath, started[0].Environment["SOC_LAUNCHER"]);
        Assert.Empty(started[1].ArgumentList);
    }

    [Fact]
    public void Sin_version_o_sin_app_lo_dice_y_no_arranca_nada()
    {
        var started = new List<System.Diagnostics.ProcessStartInfo>();
        var errors = new List<string>();

        Assert.Equal(1, Program.Run([], new FakeLauncher([]), _root, started.Add, errors.Add));
        Assert.Equal(1, Program.Run([], new FakeLauncher(new() { ["version.txt"] = () => new MemoryStream("1.0"u8.ToArray()) }), _root, started.Add, errors.Add));

        Assert.Empty(started);
        Assert.Collection(errors,
            e => Assert.Contains("version.txt", e),
            e => Assert.Contains("app.zip", e));
        Assert.All(errors, e => Assert.StartsWith("No se ha podido arrancar sOC Uninstaller:", e));
    }

    [Fact]
    public void Leer_un_recurso_y_su_falta()
    {
        Assert.Equal("2026.11.24.0\n", Program.Read(new MemoryStream(Encoding.UTF8.GetBytes("2026.11.24.0\n")), "version.txt"));

        var ex = Assert.Throws<InvalidOperationException>(() => Program.Read(null, "version.txt"));
        Assert.Contains("version.txt", ex.Message);
    }
}
