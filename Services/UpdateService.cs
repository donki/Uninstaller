using System.Text.Json;
using System.Text.Json.Serialization;

namespace Uninstaller.Services;

/// <summary>
/// Comprobacion de version al arrancar (constitucion, seccion 15): consulta un manifiesto en el
/// propio repositorio del proyecto (fuente de confianza) y, si hay una version mas reciente que la
/// instalada, avisa al usuario y le propone actualizar. Es silenciosa y no bloqueante: si no hay red
/// o ya se esta al dia, no molesta.
/// </summary>
public class UpdateService
{
    public const string AppcastUrl = "https://raw.githubusercontent.com/donki/Uninstaller/main/appcast.json";

    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly ILocalizationService _l;
    private readonly IAppEnvironment _environment;
    private readonly HttpClient _http;
    private bool _checkedThisSession;

    public UpdateService(ILocalizationService localization, IAppEnvironment environment)
        : this(localization, environment, SharedHttp)
    {
    }

    public UpdateService(ILocalizationService localization, IAppEnvironment environment, HttpClient http)
    {
        _l = localization;
        _environment = environment;
        _http = http;
    }

    public async Task CheckAndPromptAsync(IDialogService dialogs)
    {
        if (_checkedThisSession)
            return;
        _checkedThisSession = true;

        try
        {
            var json = await _http.GetStringAsync(AppcastUrl);
            var manifest = JsonSerializer.Deserialize<Appcast>(json);
            if (manifest?.Version is null)
                return;

            var current = _environment.VersionString;
            if (CompareVersions(manifest.Version, current) <= 0)
                return; // ya se esta en la ultima version (o mas nueva)

            var wantsUpdate = await dialogs.AlertAsync(
                _l["UpdateTitle"],
                string.Format(_l.CurrentCulture, _l["UpdateBody"], manifest.Version, current),
                _l["UpdateNow"], _l["UpdateLater"]);

            if (wantsUpdate && !string.IsNullOrWhiteSpace(manifest.Url))
                await _environment.OpenUrlAsync(new Uri(manifest.Url));
        }
        catch
        {
            // Sin red o manifiesto no disponible: la comprobacion no debe molestar ni bloquear.
        }
    }

    /// <summary>Compara versiones numericas por partes ("2026.07.19.0"). &gt;0 si a es mas nueva que b.</summary>
    public static int CompareVersions(string a, string b)
    {
        var pa = Parts(a);
        var pb = Parts(b);
        var n = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < n; i++)
        {
            var va = i < pa.Length ? pa[i] : 0;
            var vb = i < pb.Length ? pb[i] : 0;
            if (va != vb)
                return va.CompareTo(vb);
        }
        return 0;
    }

    private static int[] Parts(string v) =>
        v.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

    private sealed class Appcast
    {
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
    }
}
