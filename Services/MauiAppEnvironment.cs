namespace Uninstaller.Services;

/// <inheritdoc cref="IAppEnvironment"/>
public class MauiAppEnvironment : IAppEnvironment
{
    public string VersionString => AppInfo.Current.VersionString;

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
