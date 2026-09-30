using System.Text;
using Uninstaller.Services;

namespace Uninstaller.Tests;

public class UninstallCommandsTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\unins000.exe\" /LOG", "C:\\Program Files\\App\\unins000.exe", "/LOG")]
    [InlineData("  \"C:\\x\\u.exe\"  ", "C:\\x\\u.exe", "")]
    [InlineData("C:\\Program Files\\App\\uninstall.exe /S /x", "C:\\Program Files\\App\\uninstall.exe", "/S /x")]
    [InlineData("MsiExec.exe /I{1234}", "MsiExec.exe", "/I{1234}")]
    [InlineData("rundll32 shell32.dll,Foo", "rundll32", "shell32.dll,Foo")]
    [InlineData("C:\\tool\\remove", "C:\\tool\\remove", "")]
    [InlineData("\"", "\"", "")]                      // comilla sin cerrar
    public void Split(string command, string file, string arguments) =>
        Assert.Equal((file, arguments), UninstallCommands.Split(command));

    [Fact]
    public void Split_expande_variables_de_entorno()
    {
        var windir = Environment.GetEnvironmentVariable("windir") ?? Environment.GetEnvironmentVariable("SystemRoot");
        if (windir is null) return;   // fuera de Windows no aplica
        Assert.Equal((windir + "\\x.exe", "/q"), UninstallCommands.Split("%windir%\\x.exe /q"));
    }

    [Theory]
    [InlineData(1, "C:\\x\\u.exe", true)]
    [InlineData(0, "MsiExec.exe /X{1}", true)]
    [InlineData(0, "C:\\x\\u.exe", false)]
    public void IsMsi(int flag, string uninstall, bool expected) => Assert.Equal(expected, UninstallCommands.IsMsi(flag, uninstall));

    [Theory]
    [InlineData("unins000.exe", "... Inno Setup ...", false, InstallerKind.InnoSetup)]
    [InlineData("unins000.exe", "nada", true, InstallerKind.InnoSetup)]
    [InlineData("unins000.exe", "nada", false, InstallerKind.Unknown)]
    [InlineData("uninstall.exe", "Nullsoft Install System", false, InstallerKind.Nsis)]
    [InlineData("uninstall.exe", "NSIS Error", false, InstallerKind.Nsis)]
    [InlineData("setup.exe", "inno setup", true, InstallerKind.Unknown)]   // la marca distingue mayusculas; sin «unins» delante
    public void DetectFromHeader(string file, string header, bool dat, InstallerKind expected) =>
        Assert.Equal(expected, UninstallCommands.DetectFromHeader(file, header, dat));

    [Theory]
    [InlineData(null, InstallerKind.Unknown, false)]
    [InlineData("x /quiet", InstallerKind.Unknown, true)]
    [InlineData(null, InstallerKind.Msi, true)]
    [InlineData(null, InstallerKind.Nsis, true)]
    public void SupportsUnattended(string? quiet, InstallerKind kind, bool expected) =>
        Assert.Equal(expected, UninstallCommands.SupportsUnattended(quiet, kind));

    [Fact]
    public void Silenciosa_del_registro_manda_si_es_desatendido()
    {
        Assert.Equal(("C:\\a\\q.exe", "/silent"),
            UninstallCommands.Build("C:\\a\\u.exe", "\"C:\\a\\q.exe\" /silent", InstallerKind.Nsis, unattended: true));
        Assert.Equal(("C:\\a\\u.exe", ""),
            UninstallCommands.Build("C:\\a\\u.exe", "\"C:\\a\\q.exe\" /silent", InstallerKind.Nsis, unattended: false));
    }

    [Theory]
    [InlineData("MsiExec.exe /I{G}", false, "/X{G}")]                      // «modificar» pasa a «quitar»
    [InlineData("MsiExec.exe /I{G}", true, "/X{G} /qn /norestart")]
    [InlineData("MsiExec.exe /X{G}", true, "/X{G} /qn /norestart")]
    [InlineData("MsiExec.exe /i{G} /x", false, "/i{G} /x")]                 // ya pide quitar: no se toca
    public void Msi(string uninstall, bool unattended, string arguments) =>
        Assert.Equal(("MsiExec.exe", arguments), UninstallCommands.Build(uninstall, null, InstallerKind.Msi, unattended));

    [Theory]
    [InlineData(InstallerKind.InnoSetup, true, "/LOG /VERYSILENT /SUPPRESSMSGBOXES /NORESTART")]
    [InlineData(InstallerKind.Nsis, true, "/LOG /S")]
    [InlineData(InstallerKind.Unknown, true, "/LOG")]      // desconocido: con su asistente
    [InlineData(InstallerKind.InnoSetup, false, "/LOG")]
    public void Inno_y_Nsis(InstallerKind kind, bool unattended, string arguments) =>
        Assert.Equal(("C:\\a\\u.exe", arguments), UninstallCommands.Build("\"C:\\a\\u.exe\" /LOG", null, kind, unattended));

    [Fact]
    public void Sin_argumentos_no_deja_espacios()
    {
        Assert.Equal(("C:\\a\\u.exe", "/S"), UninstallCommands.Build("C:\\a\\u.exe", null, InstallerKind.Nsis, true));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("\"C:\\App\\app.exe\",0", "C:\\App\\app.exe")]
    [InlineData("\"", null)]
    [InlineData("C:\\App\\app.exe,-101", "C:\\App\\app.exe")]
    [InlineData("C:\\App\\app.exe /uninstall", "C:\\App\\app.exe")]
    [InlineData("C:\\App\\icon.ico", "C:\\App\\icon.ico")]
    [InlineData("C:\\App\\res.dll,3", "C:\\App\\res.dll")]
    [InlineData("C:\\App\\res.dll extra", "C:\\App\\res.dll")]
    [InlineData("C:\\App\\icon.ico,x", "C:\\App\\icon.ico")]
    [InlineData("MsiExec.exe /X{G}", null)]
    [InlineData("C:\\App\\sin-extension", "C:\\App\\sin-extension")]
    public void IconFile(string? value, string? expected) => Assert.Equal(expected, UninstallCommands.IconFile(value));

    [Theory]
    [InlineData("20240517", 2024, 5, 17)]
    public void ParseInstallDate(string raw, int y, int m, int d) =>
        Assert.Equal(new DateTime(y, m, d), UninstallCommands.ParseInstallDate(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("2024517")]
    [InlineData("20241317")]
    [InlineData("17/05/2024")]
    public void ParseInstallDate_invalida(string? raw) => Assert.Null(UninstallCommands.ParseInstallDate(raw));
}

public class RecycleBinPathsTests
{
    [Theory]
    [InlineData("C:\\$Recycle.Bin", true)]
    [InlineData("C:\\$RECYCLE.BIN\\S-1-5-21-1", true)]
    [InlineData("D:\\RECYCLER\\S-1-5", true)]
    [InlineData("C:\\$Recycle.Bin\\S-1-5-21-1\\$RABC.txt", false)]
    [InlineData("C:\\Users", false)]
    [InlineData("C:\\", false)]
    [InlineData("C:\\Datos\\$Recycle.Bin", false)]
    public void IsBinFolder(string path, bool expected) => Assert.Equal(expected, RecycleBinPaths.IsBinFolder(path));

    [Theory]
    [InlineData("C:\\$Recycle.Bin\\S-1-5-21-9\\", "S-1-5-21-9")]
    [InlineData("C:/$Recycle.Bin/S-1-5-21-9", "S-1-5-21-9")]
    [InlineData("C:\\$Recycle.Bin", null)]
    [InlineData("C:\\Users\\S-1-5-21-9", null)]
    [InlineData("C:\\$Recycle.Bin\\S-1\\$R1", null)]
    public void OwnerSid(string path, string? expected) => Assert.Equal(expected, RecycleBinPaths.OwnerSid(path));

    [Theory]
    [InlineData("C:\\$Recycle.Bin\\S-1\\$RAB.pdf", true)]
    [InlineData("C:\\$Recycle.Bin\\S-1", true)]
    [InlineData("C:\\$Recycle.Bin", false)]
    [InlineData("\\\\srv\\RECYCLER\\x", true)]
    [InlineData("C:\\Datos\\x", false)]
    public void IsInside(string path, bool expected) => Assert.Equal(expected, RecycleBinPaths.IsInside(path));

    [Theory]
    [InlineData("C:\\$Recycle.Bin\\S-1\\$RA1B2C3.pdf", "C:\\$Recycle.Bin\\S-1\\$IA1B2C3.pdf")]
    [InlineData("C:\\$Recycle.Bin\\S-1\\$rXYZ", "C:\\$Recycle.Bin\\S-1\\$IXYZ")]
    [InlineData("C:\\$Recycle.Bin\\S-1\\$IA1B2C3.pdf", null)]
    [InlineData("C:\\Datos\\factura.pdf", null)]
    [InlineData("$RABC", null)]
    public void InfoFileFor(string path, string? expected) => Assert.Equal(expected, RecycleBinPaths.InfoFileFor(path));

    private static byte[] InfoV2(string original, int? declaredChars = null)
    {
        var name = Encoding.Unicode.GetBytes(original + "\0");
        return [.. BitConverter.GetBytes(2L), .. BitConverter.GetBytes(12345L), .. BitConverter.GetBytes(0L),
                .. BitConverter.GetBytes(declaredChars ?? original.Length + 1), .. name];
    }

    private static byte[] InfoV1(string original, bool garbageAfterNul = false)
    {
        var path = new byte[520];
        Encoding.Unicode.GetBytes(original).CopyTo(path, 0);
        if (garbageAfterNul)
            Encoding.Unicode.GetBytes("BASURA").CopyTo(path, (original.Length + 1) * 2);
        return [.. BitConverter.GetBytes(1L), .. BitConverter.GetBytes(12345L), .. BitConverter.GetBytes(0L), .. path];
    }

    [Fact]
    public void Ficha_version_2() =>
        Assert.Equal("factura.pdf", RecycleBinPaths.OriginalName(InfoV2("C:\\Users\\Ana\\Documents\\factura.pdf")));

    [Fact]
    public void Ficha_version_2_de_una_carpeta() =>
        Assert.Equal("Fotos", RecycleBinPaths.OriginalName(InfoV2("D:\\Fotos\\")));

    [Fact]
    public void Ficha_version_1() =>
        Assert.Equal("viejo.txt", RecycleBinPaths.OriginalName(InfoV1("C:\\viejo.txt")));

    [Fact]
    public void Ficha_version_1_con_relleno_sucio_tras_el_nulo() =>
        Assert.Equal("viejo.txt", RecycleBinPaths.OriginalName(InfoV1("C:\\viejo.txt", garbageAfterNul: true)));

    [Fact]
    public void Ficha_con_longitud_mentirosa_lee_lo_que_hay()
    {
        Assert.Equal("a.txt", RecycleBinPaths.OriginalName(InfoV2("C:\\a.txt", declaredChars: 9999)));
        Assert.Equal("a.txt", RecycleBinPaths.OriginalName(InfoV2("C:\\a.txt", declaredChars: -1)));
    }

    [Fact]
    public void Ficha_rota_o_vacia()
    {
        Assert.Null(RecycleBinPaths.OriginalName([]));
        Assert.Null(RecycleBinPaths.OriginalName(new byte[25]));
        Assert.Null(RecycleBinPaths.OriginalName([.. BitConverter.GetBytes(2L), .. new byte[19]]));   // v2 de 27 bytes
        Assert.Null(RecycleBinPaths.OriginalName(InfoV2("")));
        Assert.Null(RecycleBinPaths.OriginalName(InfoV1("")));
    }
}

public class ProtectedFoldersTests
{
    private static readonly ProtectedFolders Folders = new(
        "C:\\Windows\\", "C:\\Program Files", "C:\\Program Files (x86)", "C:\\ProgramData", "C:\\Users\\Ana");

    [Theory]
    [InlineData("C:\\Windows", "DiskRiskWindows")]
    [InlineData("c:\\windows\\System32\\drivers", "DiskRiskWindows")]
    [InlineData("C:\\Program Files", "DiskRiskPrograms")]
    [InlineData("C:\\Program Files\\App", "DiskRiskPrograms")]
    [InlineData("C:\\Program Files\\App\\cache", null)]            // dentro de un programa ya no se avisa
    [InlineData("C:\\Program Files (x86)\\Old\\", "DiskRiskPrograms")]
    [InlineData("C:\\ProgramData\\Vendor", "DiskRiskProgramData")]
    [InlineData("C:\\ProgramData\\Vendor\\logs", null)]
    [InlineData("C:\\Users", "DiskRiskProfile")]
    [InlineData("C:\\Users\\Otro", "DiskRiskProfile")]
    [InlineData("C:\\Users\\Ana", "DiskRiskProfile")]
    [InlineData("C:\\Users\\Ana\\AppData\\Local", "DiskRiskProfile")]
    [InlineData("C:\\Users\\Ana\\AppData\\Local\\Temp", null)]
    [InlineData("C:\\Users\\Ana\\Downloads", null)]
    [InlineData("C:\\$Recycle.Bin", "DiskRiskSystem")]
    [InlineData("D:\\System Volume Information", "DiskRiskSystem")]
    [InlineData("C:\\pagefile.sys", "DiskRiskSystem")]
    [InlineData("C:\\Datos\\Boot", null)]                        // solo cuenta en la raiz
    [InlineData("C:\\Windows.old", null)]                        // no es hija de Windows
    [InlineData("D:\\Juegos", null)]
    [InlineData("C:\\", null)]
    public void Risk(string path, string? expected) => Assert.Equal(expected, Folders.Risk(path));

    [Fact]
    public void Sin_perfil_ni_rutas_no_protege_nada_de_mas()
    {
        var empty = new ProtectedFolders("", "", "", "", "");
        Assert.Null(empty.Risk("C:\\Users\\Ana"));
        Assert.Equal("DiskRiskSystem", empty.Risk("E:\\Recovery"));
    }

    [Fact]
    public void Las_del_equipo_protegen_Windows()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length == 0) return;   // fuera de Windows no aplica
        Assert.Equal("DiskRiskWindows", ProtectedFolders.ForCurrentUser().Risk(windows));
    }
}
