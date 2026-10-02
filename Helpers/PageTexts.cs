using Uninstaller.Services;

namespace Uninstaller.Helpers;

/// <summary>
/// Vuelca los textos fijos de una pagina: cada control (por su x:Name) recibe el texto de su clave.
/// La lista de controles y claves vive en la logica de cada pantalla, donde se prueba.
/// </summary>
public static class PageTexts
{
    public static void Apply(Element page, IReadOnlyDictionary<string, string> texts, ILocalizationService l)
    {
        foreach (var (name, key) in texts)
        {
            switch (page.FindByName(name))
            {
                case Label label: label.Text = l[key]; break;
                case Button button: button.Text = l[key]; break;
                default: throw new InvalidOperationException($"No text control named '{name}'.");
            }
        }
    }
}
