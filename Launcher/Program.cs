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
    private static int Main(string[] args) => Run(
        args,
        Assembly.GetExecutingAssembly(),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        info => Process.Start(info),
        message => MessageBox(IntPtr.Zero, message, "sOC Uninstaller", 0x10));

    /// <summary>
    /// Desempaqueta (si hace falta) la version que lleva <paramref name="assembly"/> dentro de
    /// <paramref name="localAppData"/> y arranca la aplicacion con los mismos argumentos. Si algo
    /// falla, lo dice con <paramref name="error"/> y devuelve 1.
    /// </summary>
    internal static int Run(string[] args, Assembly assembly, string localAppData, Action<ProcessStartInfo> start, Action<string> error)
    {
        try
        {
            var version = Read(assembly.GetManifestResourceStream("version.txt"), "version.txt").Trim();
            var root = Path.Combine(localAppData, "sOCUninstaller", "app");
            var target = Path.Combine(root, version);
            var exe = Path.Combine(target, "Uninstaller.exe");

            if (NeedsUnpack(target))
            {
                using var zip = assembly.GetManifestResourceStream("app.zip") ?? throw new InvalidOperationException("Falta app.zip dentro del exe.");
                Unpack(zip, root, target);
            }

            var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = target };
            // Para que la ventana se pueda anclar a la barra de tareas apuntando a ESTE exe.
            info.Environment["SOC_LAUNCHER"] = Environment.ProcessPath ?? string.Empty;
            foreach (var a in args)
                info.ArgumentList.Add(a);
            start(info);
            return 0;
        }
        catch (Exception ex)
        {
            error("No se ha podido arrancar sOC Uninstaller:\n" + ex.Message);
            return 1;
        }
    }

    /// <summary>Hace falta desempaquetar si no esta el exe o si la vez anterior no termino (sin la marca).</summary>
    internal static bool NeedsUnpack(string target) =>
        !File.Exists(Path.Combine(target, "Uninstaller.exe")) || !File.Exists(Path.Combine(target, ".completa"));

    /// <summary>Desempaqueta a una carpeta temporal y la renombra al final: nunca queda a medias.</summary>
    internal static void Unpack(Stream zip, string root, string target)
    {
        Directory.CreateDirectory(root);
        var temp = target + ".nuevo";
        if (Directory.Exists(temp))
            Directory.Delete(temp, recursive: true);
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true))
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

    internal static string Read(Stream? resource, string name)
    {
        using var stream = resource ?? throw new InvalidOperationException("Falta " + name + " dentro del exe.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
