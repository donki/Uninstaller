namespace Uninstaller.Services;

/// <inheritdoc cref="IDialogService"/>
public class PageDialogService(Page page) : IDialogService
{
    public Task<bool> AlertAsync(string title, string message, string accept, string? cancel = null) =>
        SocShared.ModernDialog.AlertAsync(page, title, message, accept, cancel);

    public Task<string?> ActionSheetAsync(string? title, string cancel, params string[] options) =>
        SocShared.ModernDialog.ActionSheetAsync(page, title, cancel, options);

    public Task<string?> PromptAsync(string title, string? message, string accept, string cancel, string? initialValue = null) =>
        SocShared.ModernDialog.PromptAsync(page, title, message, accept: accept, cancel: cancel, initialValue: initialValue);
}
