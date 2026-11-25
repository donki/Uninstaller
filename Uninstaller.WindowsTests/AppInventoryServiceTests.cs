using Microsoft.Win32;
using Uninstaller.Models;
using Uninstaller.Platforms.Windows;
using Uninstaller.Services;
using Windows.Management.Deployment;

namespace Uninstaller.WindowsTests;

/// <summary>
/// El inventario de Windows sobre una clave Uninstall propia de la prueba (en HKCU, se borra al
/// acabar) y la desinstalacion que espera a que la clave desaparezca.
/// </summary>
[Collection("Windows")]
public sealed class AppInventoryServiceTests : IDisposable
{
    private readonly string _root = @"Software\sOCUninstallerTests\" + Guid.NewGuid().ToString("N");
    private readonly string _keys;
    private readonly string _dir = Directory.CreateTempSubdirectory("uninst-inv-").FullName;
    private readonly TimeSpan _poll = AppInventoryService.UninstallPoll;
    private static readonly string Notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
    private static readonly string Rundll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "rundll32.exe");

    public AppInventoryServiceTests()
    {
        WinUIHost.Ensure();
        _keys = _root + @"\Uninstall";
        AppInventoryService.UninstallPoll = TimeSpan.FromMilliseconds(10);
    }

    public void Dispose()
    {
        AppInventoryService.UninstallPoll = _poll;
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private void Program(string key, params (string Name, object Value)[] values)
    {
        using var k = Registry.CurrentUser.CreateSubKey(_keys + "\\" + key);
        foreach (var (name, value) in values)
            k.SetValue(name, value);
    }

    private AppInventoryService Service(Func<IEnumerable<global::Windows.ApplicationModel.Package>>? packages = null) =>
        new([(Registry.CurrentUser, _keys), (Registry.CurrentUser, _root + @"\NoExiste")], packages ?? (() => []));

    [Fact]
    public async Task Programas_del_registro()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "Programa")).FullName;
        File.WriteAllBytes(Path.Combine(folder, "datos.bin"), new byte[4096]);
        Program("Editor", ("DisplayName", "Editor"), ("DisplayVersion", "2.1"), ("Publisher", "Pruebas"),
            ("InstallDate", "20260115"), ("EstimatedSize", 2), ("DisplayIcon", Notepad + ",0"),
            ("UninstallString", "\"" + Rundll + "\""));
        Program("SinFecha", ("DisplayName", "Sin fecha"), ("UninstallString", Rundll), ("InstallLocation", folder));
        Program("SinNada", ("DisplayName", "Sin nada"), ("UninstallString", Rundll), ("InstallLocation", Path.Combine(_dir, "no-esta")),
            ("DisplayIcon", Path.Combine(_dir, "no-esta.ico")));
        Program("IconoRaro", ("DisplayName", "Icono raro"), ("UninstallString", Rundll), ("DisplayIcon", Path.Combine(_dir, "texto.ico")));
        File.WriteAllText(Path.Combine(_dir, "texto.ico"), "no es un icono");
        Program("Componente", ("DisplayName", "Componente"), ("UninstallString", Rundll), ("SystemComponent", 1));
        Program("Parche", ("DisplayName", "KB1"), ("UninstallString", Rundll), ("ParentKeyName", "Editor"));
        Program("SinNombre", ("UninstallString", Rundll));

        var apps = await Service().GetInstalledAppsAsync(includeSystem: false);

        Assert.Equal(["Editor 2.1", "Icono raro", "Sin fecha", "Sin nada"], apps.Select(a => a.Label).Order());
        Assert.Equal(apps.OrderByDescending(a => a.InstallDate).Select(a => a.Label), apps.Select(a => a.Label));
        var editor = apps.Single(a => a.Label == "Editor 2.1");
        Assert.Equal("win32:Editor", editor.PackageName);
        Assert.Equal(new DateTime(2026, 1, 15), editor.InstallDate);
        Assert.Equal(2048, editor.SizeBytes);
        Assert.Equal("Pruebas", editor.Publisher);
        Assert.NotNull(editor.Icon);
        // Sin InstallDate: la fecha de la carpeta; sin EstimatedSize: lo que ocupa.
        var sinFecha = apps.Single(a => a.Label == "Sin fecha");
        Assert.Equal(Directory.GetCreationTime(folder), sinFecha.InstallDate);
        Assert.Equal(4096, sinFecha.SizeBytes);
        Assert.NotNull(sinFecha.Icon);                // el del ejecutable de desinstalar
        Assert.Equal(DateTime.MinValue, apps.Single(a => a.Label == "Sin nada").InstallDate);

        var all = await Service().GetInstalledAppsAsync(includeSystem: true);
        Assert.True(all.Single(a => a.Label == "Componente").IsSystem);
    }

    [Fact]
    public async Task Desinstalar_espera_a_que_la_clave_desaparezca()
    {
        Program("Quitado", ("DisplayName", "Quitado"), ("UninstallString", Rundll));
        Program("Terco", ("DisplayName", "Terco"), ("UninstallString", Rundll), ("QuietUninstallString", "\"" + Rundll + "\""));
        var service = Service();
        await service.GetInstalledAppsAsync(false);

        // El desinstalador (aqui, uno que no hace nada) termina y la clave ya no esta: desinstalado.
        Registry.CurrentUser.DeleteSubKeyTree(_keys + @"\Quitado");
        Assert.True(await service.UninstallAsync("win32:Quitado", unattended: false));

        // La clave sigue ahi pasados los reintentos: no desinstalado.
        Assert.False(await service.UninstallAsync("win32:Terco", unattended: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UninstallAsync("win32:Desconocido", false));
    }

    [Fact]
    public async Task Un_paquete_que_no_existe_no_se_puede_quitar()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => Service().UninstallAsync("msix:sOC.NoExiste_1.0.0.0_x64__0000000000000", false));
    }

    /// <summary>
    /// Paquetes MSIX de verdad, solo leidos: los del propio sistema (firmados por Windows), que estan
    /// en cualquier equipo; nunca se toca ninguno.
    /// </summary>
    [Fact]
    public async Task Paquetes_del_sistema_solo_si_se_piden()
    {
        var system = new PackageManager().FindPackagesForUser(string.Empty)
            .Where(p => p.SignatureKind == global::Windows.ApplicationModel.PackageSignatureKind.System)
            .Take(15).ToList();
        Assert.NotEmpty(system);
        var service = Service(() => system);

        Assert.DoesNotContain(await service.GetInstalledAppsAsync(includeSystem: false), a => a.PackageName.StartsWith("msix:"));
        var apps = (await service.GetInstalledAppsAsync(includeSystem: true)).Where(a => a.PackageName.StartsWith("msix:")).ToList();
        Assert.NotEmpty(apps);
        Assert.All(apps, a =>
        {
            Assert.True(a.IsSystem);
            Assert.True(a.SupportsUnattended);
            Assert.Matches(@" \d+\.\d+\.\d+\.\d+$", a.Label);
        });
    }

    [Fact]
    public void El_constructor_normal_lee_las_claves_del_sistema()
    {
        Assert.NotNull(new AppInventoryService());
    }
}
