using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Uninstaller.Services;

namespace Uninstaller.Tests;

/// <summary>Lo que el sistema no deja leer: un desinstalador en uso y una carpeta sin permiso para listarla.</summary>
[SupportedOSPlatform("windows")]
public sealed class SystemErrorsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uninst-err-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Un_desinstalador_que_no_se_puede_leer_es_desconocido()
    {
        var exe = Path.Combine(_dir, "unins000.exe");
        File.WriteAllText(exe, "Inno Setup");
        using (new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(InstallerKind.Unknown, UninstallRegistry.DetectInstaller("\"" + exe + "\""));
    }

    [Fact]
    public void Carpeta_sin_permiso_para_listarla_queda_marcada_y_el_resto_se_cuenta()
    {
        var locked = Directory.CreateDirectory(Path.Combine(_dir, "Cerrada")).FullName;
        File.WriteAllBytes(Path.Combine(locked, "dentro.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_dir, "fuera.bin"), new byte[50]);
        var info = new DirectoryInfo(locked);
        var me = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(me, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var security = info.GetAccessControl();
        security.AddAccessRule(deny);
        info.SetAccessControl(security);
        try
        {
            var root = DiskScanner.ScanAsync(_dir, null, CancellationToken.None).GetAwaiter().GetResult();

            var node = Assert.Single(root.Children);
            Assert.True(node.Inaccessible);
            Assert.Equal(50, root.Size);
        }
        finally
        {
            security.RemoveAccessRule(deny);
            info.SetAccessControl(security);
        }
    }
}
