using System.Runtime.InteropServices;

namespace Uninstaller.Platforms.Windows;

/// <summary>
/// Identidad de la ventana para la barra de tareas cuando la aplicacion arranca por un lanzador.
/// La ventana es del exe interno (en %LOCALAPPDATA%\…\app\version), asi que «anclar a la barra de
/// tareas» apuntaba a un exe de carpeta versionada y no funcionaba. Con un AppUserModelID y las
/// propiedades de relanzamiento (comando, nombre e icono) la barra ancla el lanzador, que es lo
/// que el usuario copio y lo que sobrevive a las actualizaciones.
/// </summary>
internal static class TaskbarIdentity
{
    public static void Apply(IntPtr hwnd, string appUserModelId, string displayName, string? launcherPath)
    {
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store is null)
                return;
            SetString(store, PKEY_AppUserModel_ID, appUserModelId);
            if (!string.IsNullOrEmpty(launcherPath) && File.Exists(launcherPath))
            {
                SetString(store, PKEY_AppUserModel_RelaunchCommand, "\"" + launcherPath + "\"");
                SetString(store, PKEY_AppUserModel_RelaunchDisplayNameResource, displayName);
                SetString(store, PKEY_AppUserModel_RelaunchIconResource, launcherPath + ",0");
            }
            store.Commit();
            Marshal.ReleaseComObject(store);
        }
        catch (Exception)
        {
            // Sin esto la aplicacion funciona igual; solo el anclaje pierde.
        }
    }

    private static void SetString(IPropertyStore store, PropertyKey key, string value)
    {
        var v = new PropVariant { vt = 31 /* VT_LPWSTR */, p = Marshal.StringToCoTaskMemUni(value) };
        try { store.SetValue(ref key, ref v); }
        finally { Marshal.FreeCoTaskMem(v.p); }
    }

    private static readonly Guid AppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private static readonly PropertyKey PKEY_AppUserModel_ID = new(AppUserModel, 5);
    private static readonly PropertyKey PKEY_AppUserModel_RelaunchCommand = new(AppUserModel, 2);
    private static readonly PropertyKey PKEY_AppUserModel_RelaunchIconResource = new(AppUserModel, 3);
    private static readonly PropertyKey PKEY_AppUserModel_RelaunchDisplayNameResource = new(AppUserModel, 4);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey(Guid fmtid, uint pid)
    {
        public Guid fmtid = fmtid;
        public uint pid = pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort r1, r2, r3;
        public IntPtr p;
        public IntPtr p2;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);
}
