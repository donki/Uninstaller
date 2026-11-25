using Uninstaller.Services;

namespace Uninstaller.WindowsTests;

/// <summary>La version que enseña «Acerca de» en Windows: la InformationalVersion, no la del exe.</summary>
public sealed class AppEnvironmentTests
{
    [Fact]
    public void En_Windows_la_version_es_la_InformationalVersion()
    {
        // El proyecto de pruebas la fija a 2026.11.25.0 (en la app, ApplicationDisplayVersion).
        Assert.Equal("2026.11.25.0", MauiAppEnvironment.DisplayVersion);
        Assert.Equal("2026.11.25.0", new MauiAppEnvironment().VersionString);
    }
}
