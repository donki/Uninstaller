using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Uninstaller.Platforms.Windows;

/// <summary>Un programa con el que Windows sabe abrir un tipo de fichero (Bloc de notas, VS Code, Word…).</summary>
public sealed record OpenWithApp(string Name, string? IconFile, Action<string> Open);

/// <summary>
/// Abrir ficheros y carpetas con los programas del sistema: el predeterminado, los que Windows tiene
/// asignados a la extension (lo mismo que su «Abrir con»), el dialogo de elegir otra aplicacion y el
/// menu contextual completo del Explorador («Mostrar mas opciones»), donde salen tambien las
/// entradas que añaden los programas (VS Code, 7-Zip…).
/// </summary>
public static class ShellOpen
{
    /// <summary>Con el programa predeterminado (una carpeta, en el Explorador).</summary>
    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>Los programas recomendados para esa extension, como en el «Abrir con» de Windows.</summary>
    public static IReadOnlyList<OpenWithApp> AppsFor(string file)
    {
        var list = new List<OpenWithApp>();
        var extension = Path.GetExtension(file);
        if (extension.Length == 0)
            return list;
        try
        {
            SHAssocEnumHandlers(extension, 1 /* ASSOC_FILTER_RECOMMENDED */, out var handlers);
            var one = new IAssocHandler[1];
            while (handlers.Next(1, one, out var fetched) == 0 && fetched == 1)
            {
                var handler = one[0];
                try
                {
                    handler.GetUIName(out var name);
                    handler.GetName(out var exe);
                    string? icon = null;
                    try
                    {
                        handler.GetIconLocation(out var iconPath, out var index);
                        icon = IconFile(iconPath, index);
                    }
                    catch (Exception) { }
                    // Sin icono propio, el del ejecutable.
                    icon ??= IconFile(exe, 0);
                    name = FriendlyName(name, exe);
                    if (!string.IsNullOrWhiteSpace(name) && list.All(a => a.Name != name))
                        list.Add(new OpenWithApp(name, icon, path => Invoke(handler, path)));
                }
                catch (Exception) { }
            }
        }
        catch (Exception)
        {
            // Sin programas asignados (o el sistema no responde): el submenu solo ofrece elegir otro.
        }
        return list;
    }

    /// <summary>
    /// Algunos programas no se registran con nombre y Windows da la ruta del ejecutable
    /// («C:\Program Files\Calibre2\calibre.exe»): entonces, la descripcion del exe o su nombre.
    /// </summary>
    private static string FriendlyName(string name, string exe)
    {
        if (!string.IsNullOrWhiteSpace(name) && !name.Contains('\\'))
            return name;
        var path = string.IsNullOrWhiteSpace(name) ? exe : name;
        try
        {
            path = path.Trim('"');
            if (File.Exists(path) && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                return description;
        }
        catch (Exception) { }
        return Path.GetFileNameWithoutExtension(path.Trim('"'));
    }

    /// <summary>El dialogo de Windows «¿Como quieres abrir este archivo?».</summary>
    public static void ChooseApp(IntPtr owner, string file)
    {
        var info = new OpenAsInfo { File = file, Flags = 0x1 | 0x4 };   // OAIF_ALLOW_REGISTRATION | OAIF_EXEC
        SHOpenWithDialog(owner, ref info);
    }

    private static void Invoke(IAssocHandler handler, string path)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out var item);
        item.BindToHandler(IntPtr.Zero, BhidDataObject, IidDataObject, out var data);
        try { handler.Invoke(data); }
        finally { Marshal.Release(data); }
    }

    // ------------------------------------------------------------------ menu del Explorador

    private static IContextMenu2? _menu2;
    private static IContextMenu3? _menu3;
    private static IntPtr _previousProc;
    private static readonly WndProc HookProc = Hook;

    /// <summary>
    /// El menu contextual del Explorador para esa ruta, junto al puntero. Las entradas con submenu
    /// («Abrir con», «Enviar a») se rellenan al abrirlas: para eso, mientras el menu esta abierto, los
    /// mensajes de menu de la ventana van a la propia extension del shell.
    /// </summary>
    public static void ShowExplorerMenu(IntPtr owner, string path)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out var item);
        item.BindToHandler(IntPtr.Zero, BhidSfUiObject, typeof(IContextMenu).GUID, out var raw);
        var menu = (IContextMenu)Marshal.GetObjectForIUnknown(raw);
        Marshal.Release(raw);
        var popup = CreatePopupMenu();
        try
        {
            const uint first = 1, last = 0x7FFF;
            if (menu.QueryContextMenu(popup, 0, first, last, 0 /* CMF_NORMAL */) < 0)
                return;
            _menu2 = menu as IContextMenu2;
            _menu3 = menu as IContextMenu3;
            _previousProc = SetWindowLongPtr(owner, -4 /* GWLP_WNDPROC */, Marshal.GetFunctionPointerForDelegate(HookProc));
            uint command;
            try
            {
                GetCursorPos(out var p);
                command = TrackPopupMenuEx(popup, 0x0100 /* TPM_RETURNCMD */ | 0x0002 /* TPM_RIGHTBUTTON */, p.X, p.Y, owner, IntPtr.Zero);
            }
            finally
            {
                SetWindowLongPtr(owner, -4, _previousProc);
                _menu2 = null;
                _menu3 = null;
            }
            if (command < first)
                return;
            GetCursorPos(out var at);
            var invoke = new CmInvokeCommandInfoEx
            {
                Size = Marshal.SizeOf<CmInvokeCommandInfoEx>(),
                Mask = 0x4000 /* CMIC_MASK_UNICODE */ | 0x20000000 /* CMIC_MASK_PTINVOKE */,
                Hwnd = owner,
                Verb = (IntPtr)(command - first),
                VerbW = (IntPtr)(command - first),
                Show = 1,   // SW_SHOWNORMAL
                Point = at,
            };
            menu.InvokeCommand(ref invoke);
        }
        finally
        {
            DestroyMenu(popup);
            Marshal.ReleaseComObject(menu);
        }
    }

    private static IntPtr Hook(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // WM_INITMENUPOPUP, WM_DRAWITEM, WM_MEASUREITEM y WM_MENUCHAR: los submenus del shell.
        if (msg is 0x0117 or 0x002B or 0x002C or 0x0120)
        {
            if (_menu3 is not null && _menu3.HandleMenuMsg2(msg, wParam, lParam, out var result) == 0)
                return result;
            if (_menu2 is not null && _menu2.HandleMenuMsg(msg, wParam, lParam) == 0)
                return IntPtr.Zero;
        }
        return CallWindowProc(_previousProc, hwnd, msg, wParam, lParam);
    }

    // ------------------------------------------------------------------ iconos de los programas

    private static readonly string IconDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCUninstaller", "icons");

    /// <summary>El icono del programa como PNG en disco (para el menu); null si no se puede sacar.</summary>
    private static string? IconFile(string location, int index)
    {
        if (string.IsNullOrWhiteSpace(location) || location.StartsWith('@'))
            return null;   // apps de la Store: recurso empaquetado, sin fichero de icono
        location = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"'));
        if (!File.Exists(location))
            return null;
        var file = Path.Combine(IconDir, "app-" + Convert.ToHexString(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(location + "," + index)))[..12] + ".png");
        if (File.Exists(file))
            return file;
        var small = new IntPtr[1];
        if (ExtractIconEx(location, index, null, small, 1) == 0 || small[0] == IntPtr.Zero)
            return null;
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(small[0]);
            using var bitmap = icon.ToBitmap();
            Directory.CreateDirectory(IconDir);
            bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            return file;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            DestroyIcon(small[0]);
        }
    }

    // ------------------------------------------------------------------ interop

    private static readonly Guid BhidSfUiObject = new("3981e225-f559-11d3-8e3a-00c04f6837d5");
    private static readonly Guid BhidDataObject = new("b8c0bd9f-ed24-455c-83e6-d5390c4fe8c4");
    private static readonly Guid IidDataObject = new("0000010e-0000-0000-c000-000000000046");

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string File;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Class;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CmInvokeCommandInfoEx
    {
        public int Size;
        public uint Mask;
        public IntPtr Hwnd;
        public IntPtr Verb;
        public IntPtr Parameters;
        public IntPtr Directory;
        public int Show;
        public uint HotKey;
        public IntPtr Icon;
        public IntPtr Title;
        public IntPtr VerbW;
        public IntPtr ParametersW;
        public IntPtr DirectoryW;
        public IntPtr TitleW;
        public Point Point;
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint sigdn, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr name, uint max);
    }

    [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr name, uint max);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr name, uint max);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [ComImport, Guid("973810ae-9599-4b88-9e4d-6ee98c9552da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumAssocHandlers
    {
        [PreserveSig] int Next(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IAssocHandler[] handlers, out uint fetched);
    }

    [ComImport, Guid("f04061ac-1659-4a3f-a954-775aa57fc083"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAssocHandler
    {
        void GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void GetUIName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] out string path, out int index);
        [PreserveSig] int IsRecommended();
        void MakeDefault([MarshalAs(UnmanagedType.LPWStr)] string description);
        void Invoke(IntPtr dataObject);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHAssocEnumHandlers(string extension, int filter, out IEnumAssocHandlers handlers);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(IntPtr owner, ref OpenAsInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
