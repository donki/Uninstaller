using Uninstaller.Models;
using Uninstaller.Services;

namespace Uninstaller.Tests;

/// <summary>De una clave Uninstall del registro (aqui un diccionario) a una fila de la lista.</summary>
public sealed class UninstallRegistryTests
{
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    private UninstallEntry? Read(Dictionary<string, object?> values, bool includeSystem = false, InstallerKind detected = InstallerKind.Unknown) =>
        UninstallRegistry.Read("{GUID-1}", n => values.GetValueOrDefault(n), includeSystem, _seen,
            _ => detected, raw => UninstallCommands.ParseInstallDate(raw) ?? DateTime.MinValue, (_, _) => null);

    private static Dictionary<string, object?> Program(string name = "7-Zip", string? version = "24.08") => new()
    {
        ["DisplayName"] = name,
        ["DisplayVersion"] = version,
        ["UninstallString"] = "\"C:\\Program Files\\7-Zip\\Uninstall.exe\"",
        ["Publisher"] = "Igor Pavlov",
        ["InstallDate"] = "20260115",
        ["EstimatedSize"] = 5000,
    };

    [Fact]
    public void Programa_normal()
    {
        var entry = Read(Program())!;

        Assert.Equal("win32:{GUID-1}", entry.App.PackageName);
        Assert.Equal("7-Zip 24.08", entry.App.Label);
        Assert.Equal("Igor Pavlov", entry.App.Publisher);
        Assert.Equal(new DateTime(2026, 1, 15), entry.App.InstallDate);
        Assert.Equal(entry.App.InstallDate, entry.App.UpdatedDate);
        Assert.Equal(5000 * 1024, entry.App.SizeBytes);
        Assert.False(entry.App.IsSystem);
        Assert.False(entry.App.SupportsUnattended);
        Assert.Null(entry.QuietUninstallString);
        Assert.Null(entry.FolderToMeasure);
    }

    [Fact]
    public void Sin_version_la_etiqueta_es_solo_el_nombre_y_sin_editor_queda_vacio()
    {
        var values = Program(version: null);
        values.Remove("Publisher");
        var entry = Read(values)!;
        Assert.Equal("7-Zip", entry.App.Label);
        Assert.Equal(string.Empty, entry.App.Publisher);
    }

    [Theory]
    [InlineData("DisplayName")]
    [InlineData("UninstallString")]
    public void Sin_nombre_o_sin_orden_no_es_un_programa(string missing)
    {
        var values = Program();
        values[missing] = "  ";
        Assert.Null(Read(values));
    }

    [Fact]
    public void Solo_la_orden_silenciosa_vale_como_orden_y_lo_hace_desatendible()
    {
        var values = Program();
        values.Remove("UninstallString");
        values["QuietUninstallString"] = "\"C:\\x\\unins.exe\" /S";
        var entry = Read(values)!;
        Assert.Equal("\"C:\\x\\unins.exe\" /S", entry.UninstallString);
        Assert.True(entry.App.SupportsUnattended);
    }

    [Theory]
    [InlineData("ParentKeyName", "Office")]
    [InlineData("ReleaseType", "Update")]
    [InlineData("ReleaseType", "Hotfix")]
    [InlineData("ReleaseType", "Security Update")]
    public void Actualizaciones_y_parches_no_salen(string name, string value)
    {
        var values = Program();
        values[name] = value;
        Assert.Null(Read(values));
    }

    [Fact]
    public void Componentes_del_sistema_solo_si_se_piden()
    {
        var values = Program();
        values["SystemComponent"] = 1;
        Assert.Null(Read(values));
        var entry = Read(values, includeSystem: true)!;
        Assert.True(entry.App.IsSystem);
    }

    [Fact]
    public void Repetido_en_la_clave_de_32_bits_sale_una_vez()
    {
        Assert.NotNull(Read(Program()));
        Assert.Null(Read(Program()));
        Assert.NotNull(Read(Program(version: "25.00")));
    }

    [Fact]
    public void MSI_por_la_marca_o_por_msiexec_y_si_no_lo_que_diga_la_cabecera()
    {
        var msi = Program();
        msi["WindowsInstaller"] = 1;
        Assert.Equal(InstallerKind.Msi, Read(msi)!.Installer);
        Assert.True(Read(Program("Otro"), detected: InstallerKind.InnoSetup)!.App.SupportsUnattended);
        Assert.Equal(InstallerKind.InnoSetup, Read(Program("Tercero"), detected: InstallerKind.InnoSetup)!.Installer);
    }

    [Fact]
    public void Sin_tamano_estimado_se_mide_la_carpeta_declarada()
    {
        var values = Program();
        values.Remove("EstimatedSize");
        values["InstallLocation"] = " \"C:\\Apps\\Foo\" ";
        var entry = Read(values)!;
        Assert.Equal(0, entry.App.SizeBytes);
        Assert.Equal("C:\\Apps\\Foo", entry.FolderToMeasure);
    }

    [Fact]
    public void Detectar_instalador_por_la_cabecera_del_ejecutable()
    {
        using var tree = new TempTree();
        var inno = tree.File("unins000.exe", 0);
        File.WriteAllText(inno, "MZ... Inno Setup Setup Data ...");
        var nsis = tree.File("uninstall.exe", 0);
        File.WriteAllText(nsis, "MZ... Nullsoft Install System");
        var plain = tree.File("remove.exe", 0);
        File.WriteAllText(plain, "MZ nada");

        Assert.Equal(InstallerKind.InnoSetup, UninstallRegistry.DetectInstaller($"\"{inno}\" /SILENT"));
        Assert.Equal(InstallerKind.Nsis, UninstallRegistry.DetectInstaller($"\"{nsis}\""));
        Assert.Equal(InstallerKind.Unknown, UninstallRegistry.DetectInstaller($"\"{plain}\""));
        Assert.Equal(InstallerKind.Unknown, UninstallRegistry.DetectInstaller("\"C:\\no\\existe.exe\""));
        Assert.Equal(InstallerKind.Unknown, UninstallRegistry.DetectInstaller(""));
    }

    [Fact]
    public void Medir_carpetas_suma_todo_lo_de_dentro()
    {
        using var tree = new TempTree();
        tree.File(@"A\x.bin", 100);
        tree.File(@"A\sub\y.bin", 50);
        var a = new InstalledApp { PackageName = "a", Label = "A" };
        var missing = new InstalledApp { PackageName = "b", Label = "B", SizeBytes = 7 };

        UninstallRegistry.MeasureFolders(new Dictionary<InstalledApp, string>
        {
            [a] = Path.Combine(tree.Root, "A"),
            [missing] = Path.Combine(tree.Root, "no-existe"),
        });

        Assert.Equal(150, a.SizeBytes);
        Assert.Equal(7, missing.SizeBytes);
    }
}
