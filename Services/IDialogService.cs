namespace Uninstaller.Services;

/// <summary>
/// Dialogos de la interfaz (avisos, confirmaciones, menus y entrada de texto). La implementacion real
/// usa <c>SocShared.ModernDialog</c> sobre la pagina; las pruebas usan un doble que responde solo.
/// </summary>
public interface IDialogService
{
    /// <summary>Aviso con un boton (o confirmacion si hay <paramref name="cancel"/>). True si se acepta.</summary>
    Task<bool> AlertAsync(string title, string message, string accept, string? cancel = null);

    /// <summary>Menu de opciones. Devuelve la opcion elegida, el texto de cancelar o null.</summary>
    Task<string?> ActionSheetAsync(string? title, string cancel, params string[] options);

    /// <summary>Entrada de texto. Devuelve el texto o null si se cancela.</summary>
    Task<string?> PromptAsync(string title, string? message, string accept, string cancel, string? initialValue = null);
}
