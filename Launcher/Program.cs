using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace Uninstaller.Launcher;

/// <summary>
/// Un solo exe para el usuario: lleva la aplicacion WinUI dentro (app.zip), la deja en
/// %LOCALAPPDATA%\sOCUninstaller\app\&lt;version&gt; si no esta ya esa version, y la arranca desde
/// ahi. Las versiones anteriores se borran para no acumular carpetas.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = Read(assembly, "version.txt").Trim();
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCUninstaller", "app");
            var target = Path.Combine(root, version);
            var exe = Path.Combine(target, "Uninstaller.exe");

            if (!File.Exists(exe) || !File.Exists(Path.Combine(target, ".completa")))
                Unpack(assembly, root, target);

            var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = target };
            // Para que la ventana se pueda anclar a la barra de tareas apuntando a ESTE exe.
            info.Environment["SOC_LAUNCHER"] = Environment.ProcessPath ?? string.Empty;
            foreach (var a in args)
                info.ArgumentList.Add(a);
            Process.Start(info);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero, "No se ha podido arrancar sOC Uninstaller:\n" + ex.Message, "sOC Uninstaller", 0x10);
            return 1;
        }
    }

    /// <summary>Desempaqueta a una carpeta temporal y la renombra al final: nunca queda a medias.</summary>
    private static void Unpack(Assembly assembly, string root, string target)
    {
        Directory.CreateDirectory(root);
        var temp = target + ".nuevo";
        if (Directory.Exists(temp))
            Directory.Delete(temp, recursive: true);
        using (var zip = assembly.GetManifestResourceStream("app.zip") ?? throw new InvalidOperationException("Falta app.zip dentro del exe."))
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Read))
            archive.ExtractToDirectory(temp);
        File.WriteAllText(Path.Combine(temp, ".completa"), DateTime.Now.ToString("s"));
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        Directory.Move(temp, target);

        // Las versiones anteriores sobran (si alguna esta en uso, se queda hasta la proxima).
        foreach (var old in Directory.GetDirectories(root))
        {
            if (string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
                continue;
            try { Directory.Delete(old, recursive: true); } catch (Exception) { }
        }
    }

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Falta " + name + " dentro del exe.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
