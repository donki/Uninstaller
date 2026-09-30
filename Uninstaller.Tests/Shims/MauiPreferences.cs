// Cuña de Preferences: en las pruebas no hay MAUI de verdad (la de MAUI en net10.0 lanza «no
// implementado»). Vive en memoria y por flujo asincrono, para que las pruebas no se pisen.
namespace Microsoft.Maui.Storage;

public static class Preferences
{
    private static readonly AsyncLocal<Dictionary<string, object>?> Current = new();

    private static Dictionary<string, object> Values => Current.Value ??= new Dictionary<string, object>();

    public static void Clear() => Current.Value = new Dictionary<string, object>();

    public static T Get<T>(string key, T defaultValue) =>
        Values.TryGetValue(key, out var value) ? (T)value : defaultValue;

    public static void Set<T>(string key, T value) => Values[key] = value!;
}
