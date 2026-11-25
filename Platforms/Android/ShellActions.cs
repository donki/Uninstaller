using Uninstaller.Services;

namespace Uninstaller.Platforms.Android;

/// <inheritdoc cref="PortableShellActions"/>
/// <remarks>Solo da la unidad: el almacenamiento compartido del dispositivo.</remarks>
public class ShellActions() : PortableShellActions(() => global::Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath);
