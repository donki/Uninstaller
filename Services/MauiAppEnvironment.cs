namespace Uninstaller.Services;

/// <inheritdoc cref="IAppEnvironment"/>
public class MauiAppEnvironment : IAppEnvironment
{
    public string VersionString => DisplayVersion;

    /// <summary>
    /// La version que se enseña: ApplicationDisplayVersion (2026.10.29.01). En Android es lo que da
    /// AppInfo; en Windows AppInfo da la del ejecutable o la del paquete MSIX (2026.10.2901.0), asi
    /// que se lee la InformationalVersion, que el csproj rellena con ese mismo texto.
    /// </summary>
    public static string DisplayVersion
    {
        get
        {
#if WINDOWS
            var informational = typeof(MauiAppEnvironment).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
            if (informational is { Length: > 0 })
                return informational.Split('+')[0];
#endif
            return AppInfo.Current.VersionString;
        }
    }

    public Task OpenUrlAsync(Uri uri) => Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);

    public async Task<bool> ComposeEmailAsync(string subject, string to)
    {
        try
        {
            await Email.Default.ComposeAsync(new EmailMessage { Subject = subject, To = new List<string> { to } });
            return true;
        }
        catch (FeatureNotSupportedException)
        {
            return false;
        }
    }
}
