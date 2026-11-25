using System.Diagnostics;
using System.Security.Principal;
using Uninstaller.Platforms.Windows;
using Uninstaller.Services;

namespace Uninstaller.WindowsTests;

/// <summary>Las costuras son estaticas: todas las pruebas de Windows van en serie.</summary>
[CollectionDefinition("Windows", DisableParallelization = true)]
public sealed class WindowsCollection;

/// <summary>Unidades, papelera de verdad, permisos, nombres e iconos del sistema.</summary>
[Collection("Windows")]
public sealed class ShellActionsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uninst-win-").FullName;
    private readonly string _icons;
    private readonly Func<ProcessStartInfo, Process?> _start = ShellActions.StartProcess;
    private readonly string _iconDir = ShellActions.IconDir;
    private readonly List<ProcessStartInfo> _started = [];
    private readonly ShellActions _shell = new();

    public ShellActionsTests()
    {
        WinUIHost.Ensure();
        _icons = Path.Combine(_dir, "icons");
        ShellActions.IconDir = _icons;
        ShellActions.StartProcess = info => { _started.Add(info); return null; };
    }

    public void Dispose()
    {
        ShellActions.StartProcess = _start;
        ShellActions.IconDir = _iconDir;
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string NewFile(string name, string content = "x")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Lo que se mando a la papelera, buscado por su ficha $I (nombre original).</summary>
    private static string? FindInBin(string original)
    {
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        var bin = Path.Combine(Path.GetPathRoot(original)!, "$Recycle.Bin", sid);
        foreach (var info in Directory.EnumerateFiles(bin, "$I*"))
        {
            try
            {
                if (string.Equals(RecycleBinPaths.OriginalName(File.ReadAllBytes(info)), Path.GetFileName(original), StringComparison.OrdinalIgnoreCase)
                    && File.ReadAllText(info, System.Text.Encoding.Unicode).Contains(original, StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(bin, "$R" + Path.GetFileName(info)[2..]);
            }
            catch (IOException) { }
        }
        return null;
    }

    [Fact]
    public void Las_unidades_del_equipo()
    {
        var drives = _shell.GetDrives();
        var system = Path.GetPathRoot(Environment.SystemDirectory)!;
        var c = Assert.Single(drives, d => string.Equals(d.Path, system, StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith(system, c.Label);
        Assert.True(c.TotalBytes > 0 && c.FreeBytes > 0 && c.FreeBytes <= c.TotalBytes);
    }

    [Fact]
    public void Ensenar_en_el_Explorador_carpeta_o_fichero_seleccionado()
    {
        var file = NewFile("a.txt");
        _shell.RevealInExplorer(_dir);
        _shell.RevealInExplorer(file);

        Assert.Equal(2, _started.Count);
        Assert.All(_started, s => { Assert.Equal("explorer.exe", s.FileName); Assert.True(s.UseShellExecute); });
        Assert.Equal($"\"{_dir}\"", _started[0].Arguments);
        Assert.Equal($"/select,\"{file}\"", _started[1].Arguments);
    }

    [Fact]
    public void A_la_papelera_y_de_la_papelera_para_siempre()
    {
        var a = NewFile($"uninst-prueba-{Guid.NewGuid():N}.txt", "uno");
        var b = NewFile($"uninst-prueba-{Guid.NewGuid():N}.txt", "dos");
        Assert.Empty(_shell.MoveToRecycleBin([]));

        Assert.True(_shell.MoveToRecycleBin(a));
        Assert.Empty(_shell.MoveToRecycleBin([b]));
        Assert.False(File.Exists(a));
        var inBinA = FindInBin(a);
        var inBinB = FindInBin(b);
        Assert.NotNull(inBinA);
        Assert.NotNull(inBinB);
        try
        {
            Assert.True(_shell.IsInRecycleBin(inBinA));
            Assert.False(_shell.IsInRecycleBin(_dir));
            // El nombre de dentro ($R…) no dice nada: se enseña el original, sacado de su ficha.
            Assert.Equal(Path.GetFileName(a), _shell.DisplayName(inBinA));
            var userFolder = Path.GetDirectoryName(inBinA)!;
            Assert.True(_shell.IsRecycleBinFolder(userFolder));
            Assert.Equal(Environment.UserName, _shell.RecycleBinOwner(userFolder), ignoreCase: true);
            Assert.Null(_shell.RecycleBinOwner(_dir));

            // Desde la papelera ya no se puede «mandar a la papelera»: se borra de verdad, con su ficha.
            Assert.Empty(_shell.MoveToRecycleBin([inBinA]));
            Assert.False(File.Exists(inBinA));
            Assert.Null(FindInBin(a));

            Assert.Empty(_shell.DeleteForever([inBinB], null, CancellationToken.None));
            Assert.Null(FindInBin(b));
        }
        finally
        {
            foreach (var left in new[] { FindInBin(a), FindInBin(b) }.OfType<string>())
                _shell.DeleteForever([left], null, CancellationToken.None);
        }
    }

    [Fact]
    public void Borrar_para_siempre_fuera_de_la_papelera()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "carpeta")).FullName;
        NewFile(Path.Combine("carpeta", "x.txt"));
        var file = NewFile("y.txt");
        var seen = new List<DeleteProgress>();

        Assert.Empty(_shell.DeleteForever([folder, file], new SyncProgress<DeleteProgress>(seen.Add), CancellationToken.None));
        Assert.False(Directory.Exists(folder));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Un_usuario_que_ya_no_existe_se_queda_con_su_SID()
    {
        var root = Path.GetPathRoot(_dir)!;
        const string Ghost = "S-1-5-21-1-2-3-99999";
        Assert.Equal(Ghost, _shell.RecycleBinOwner(Path.Combine(root, "$Recycle.Bin", Ghost)));
    }

    [Fact]
    public async Task Arreglar_permisos_con_takeown_e_icacls()
    {
        Assert.True(await _shell.FixPermissionsAsync([]));

        // Sin proceso (no se pudo arrancar): no arreglado.
        Assert.False(await _shell.FixPermissionsAsync([_dir]));
        var info = Assert.Single(_started);
        Assert.Equal("runas", info.Verb);
        Assert.Equal(ProcessWindowStyle.Hidden, info.WindowStyle);

        // El usuario dice que no en el control de cuentas.
        ShellActions.StartProcess = _ => throw new System.ComponentModel.Win32Exception(1223);
        Assert.False(await _shell.FixPermissionsAsync([_dir]));

        // Se ejecuta el script de verdad, sin elevar y sobre ficheros de la prueba (son del usuario).
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "sinpermiso")).FullName;
        var file = NewFile("sinpermiso.txt");
        string? script = null, scriptFile = null;
        ShellActions.StartProcess = i =>
        {
            scriptFile = i.Arguments[4..^1];
            script = File.ReadAllText(scriptFile);
            return Process.Start(new ProcessStartInfo("cmd.exe", i.Arguments) { UseShellExecute = false, CreateNoWindow = true });
        };
        Assert.True(await _shell.FixPermissionsAsync([folder + "\\", file]));

        var sid = WindowsIdentity.GetCurrent().User!.Value;
        Assert.NotNull(script);
        Assert.Contains($"takeown /F \"{folder}\" /R /D ", script);
        Assert.Contains($"icacls \"{folder}\" /grant *{sid}:(OI)(CI)F /T /C /Q", script);
        Assert.Contains($"takeown /F \"{file}\" >nul", script);
        Assert.Contains($"icacls \"{file}\" /remove:d *{sid} /C /Q", script);
        // El script temporal no se queda.
        Assert.NotNull(scriptFile);
        Assert.False(File.Exists(scriptFile));
    }

    [Fact]
    public void Carpetas_del_sistema_y_nombres_del_Explorador()
    {
        Assert.NotNull(_shell.SystemRisk(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.Null(_shell.SystemRisk(_dir));

        // Una carpeta con nombre propio (desktop.ini, como las del sistema traducidas) se enseña con
        // ese nombre; una carpeta normal se llama igual que en disco.
        var named = Directory.CreateDirectory(Path.Combine(_dir, "Programas")).FullName;
        var ini = Path.Combine(named, "desktop.ini");
        File.WriteAllText(ini, "[.ShellClassInfo]\r\nLocalizedResourceName=Carpeta de prueba\r\n", System.Text.Encoding.Unicode);
        File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(named, FileAttributes.ReadOnly | FileAttributes.Directory);
        try
        {
            Assert.Equal("Carpeta de prueba", _shell.DisplayName(named));
        }
        finally
        {
            File.SetAttributes(named, FileAttributes.Directory);
            File.SetAttributes(ini, FileAttributes.Normal);
        }
        Assert.Null(_shell.DisplayName(Directory.CreateDirectory(Path.Combine(_dir, "normal")).FullName));
        Assert.Null(_shell.DisplayName(Path.Combine(_dir, "no-esta", "nada.txt")));
        Assert.Null(_shell.DisplayName(Path.Combine(_dir, "a|b")));
    }

    [Fact]
    public void Iconos_del_sistema_en_PNG_uno_por_tipo()
    {
        var txt = NewFile("a.txt");
        var first = _shell.IconFor(txt, false);
        Assert.IsType<FileImageSource>(first);
        var png = Assert.Single(Directory.GetFiles(_icons));
        Assert.StartsWith("_txt-", Path.GetFileName(png));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, File.ReadAllBytes(png)[..4]);

        // Otro .txt reutiliza el mismo PNG; carpeta, unidad y fichero sin extension tienen el suyo.
        Assert.Equal(png, ((FileImageSource)_shell.IconFor(NewFile("b.TXT"), false)!).File);
        Assert.NotNull(_shell.IconFor(_dir, true));
        Assert.NotNull(_shell.IconFor(Path.GetPathRoot(_dir)!, true));
        Assert.NotNull(_shell.IconFor(NewFile("sinextension"), false));
        Assert.Equal(4, Directory.GetFiles(_icons).Length);
    }

    [Fact]
    public void Sin_carpeta_para_los_iconos_no_hay_icono()
    {
        ShellActions.IconDir = NewFile("no-es-carpeta");
        Assert.Null(_shell.IconFor(Path.Combine(_dir, "x.zzzsinicono" + Guid.NewGuid().ToString("N")[..4]), false));
    }

    /// <summary>Progress que informa en el momento (el normal va al contexto de sincronizacion).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
