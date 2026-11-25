using Uninstaller.Services;

namespace Uninstaller.Tests;

/// <summary>La parte de Android que no necesita Android: que paquetes salen, fechas, tamaños y la espera del dialogo.</summary>
public sealed class AndroidPackagesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uninst-apk-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string Apk(string name, int bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private static AndroidPackage Pkg(string? name, bool system = false, bool updated = false, long installed = 1_700_000_000_000,
        string? baseApk = null, IEnumerable<string?>? splits = null, Func<string?>? label = null, Func<ImageSource?>? icon = null) =>
        new(name, system, updated, installed, installed + 1000, baseApk, splits, label ?? (() => name + " app"), icon ?? (() => null));

    [Fact]
    public void Fuera_la_propia_las_sin_nombre_y_las_del_sistema_si_no_se_piden()
    {
        var packages = new[]
        {
            Pkg("com.socratic.uninstaller"),
            Pkg(null),
            Pkg(""),
            Pkg("com.android.settings", system: true),
            Pkg("com.google.maps", system: true, updated: true),   // del sistema actualizada: cuenta como del usuario
            Pkg("org.example.notes"),
        };

        var user = AndroidPackages.Build(packages, "com.socratic.uninstaller", includeSystem: false);
        Assert.Equal(["com.google.maps", "org.example.notes"], user.Select(a => a.PackageName).Order());
        Assert.All(user, a => Assert.False(a.IsSystem));

        var all = AndroidPackages.Build(packages, "com.socratic.uninstaller", includeSystem: true);
        Assert.Equal(3, all.Count);
        Assert.True(all.Single(a => a.PackageName == "com.android.settings").IsSystem);
    }

    [Fact]
    public void Nombre_icono_fechas_tamano_y_orden()
    {
        var icon = ImageSource.FromFile("x.png");
        var apk = Apk("base.apk", 1000);
        var split = Apk("split.apk", 250);
        var packages = new[]
        {
            Pkg("a.old", installed: 1_600_000_000_000),
            Pkg("b.new", installed: 1_700_000_000_000, baseApk: apk, splits: [split, null, "", Path.Combine(_dir, "no-esta.apk")],
                label: () => "Notas", icon: () => icon),
            Pkg("c.sinnombre", installed: 0, label: () => null),
            Pkg("d.falla", label: () => throw new InvalidOperationException("sin etiqueta"), icon: () => throw new InvalidOperationException("sin icono")),
        };

        var list = AndroidPackages.Build(packages, null, includeSystem: true);

        Assert.Equal(["b.new", "d.falla", "a.old", "c.sinnombre"], list.Select(a => a.PackageName));
        var b = list[0];
        Assert.Equal("Notas", b.Label);
        Assert.Same(icon, b.Icon);
        Assert.Equal(1250, b.SizeBytes);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000).LocalDateTime, b.InstallDate);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_001_000).LocalDateTime, b.UpdatedDate);
        Assert.Equal("c.sinnombre", list[3].Label);
        Assert.Equal(DateTime.MinValue, list[3].InstallDate);
        Assert.Equal("d.falla", list[1].Label);
        Assert.Null(list[1].Icon);
        Assert.Equal(0, list[1].SizeBytes);
    }

    [Fact]
    public void Fechas_fuera_de_rango_y_tamano_sin_ficheros()
    {
        Assert.Equal(DateTime.MinValue, AndroidPackages.FromMillis(0));
        Assert.Equal(DateTime.MinValue, AndroidPackages.FromMillis(-5));
        Assert.Equal(DateTime.MinValue, AndroidPackages.FromMillis(long.MaxValue));
        Assert.Equal(0, AndroidPackages.ApkSize(null, null));
        Assert.Equal(0, AndroidPackages.ApkSize("\0no valido", ["\0"]));
    }

    [Fact]
    public async Task La_espera_del_dialogo_de_desinstalar()
    {
        var waiter = new UninstallWaiter();

        var first = waiter.Start();
        var second = waiter.Start();          // la anterior se da por no desinstalada
        Assert.False(await first);

        waiter.Complete(1234, ok: true);      // resultado de otra actividad: no es el suyo
        Assert.False(second.IsCompleted);

        waiter.Complete(UninstallWaiter.RequestCode, ok: true);
        Assert.True(await second);

        waiter.Complete(UninstallWaiter.RequestCode, ok: false);   // sin nadie esperando: no pasa nada
        var third = waiter.Start();
        waiter.Complete(UninstallWaiter.RequestCode, ok: false);
        Assert.False(await third);
    }

    [Fact]
    public async Task Acciones_sin_Explorador_borran_directamente()
    {
        var shell = new PortableShellActions(() => "/storage/emulated/0");
        var drive = Assert.Single(shell.GetDrives());
        Assert.Equal("/storage/emulated/0", drive.Path);
        Assert.Empty(new PortableShellActions(() => null).GetDrives());

        var a = Apk("a.txt", 3);
        var b = Apk("b.txt", 3);
        var dir = Directory.CreateDirectory(Path.Combine(_dir, "carpeta")).FullName;
        File.WriteAllText(Path.Combine(dir, "x.txt"), "x");
        Assert.True(shell.MoveToRecycleBin(a));
        Assert.False(File.Exists(a));
        Assert.False(shell.MoveToRecycleBin("\0no valido"));
        Assert.Equal(["\0no valido"], shell.MoveToRecycleBin([b, "\0no valido"]));
        Assert.Empty(shell.DeleteForever([dir], null, CancellationToken.None));
        Assert.False(Directory.Exists(dir));

        shell.RevealInExplorer(_dir);
        Assert.False(await shell.FixPermissionsAsync([_dir]));
        Assert.Null(shell.SystemRisk(_dir));
        Assert.Null(shell.IconFor(_dir, true));
        Assert.False(shell.IsInRecycleBin(_dir));
        Assert.Null(shell.DisplayName(_dir));
        Assert.False(shell.IsRecycleBinFolder(_dir));
        Assert.Null(shell.RecycleBinOwner(_dir));
    }
}
