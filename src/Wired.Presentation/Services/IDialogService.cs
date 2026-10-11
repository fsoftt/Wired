namespace Wired.Presentation.Services;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);

    /// <returns>The text entered, or null when canceled.</returns>
    Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? initialValue = null, int maxLength = -1);

    /// <returns>Index of the chosen option, or null when canceled.</returns>
    Task<int?> ChooseAsync(string title, string cancel, IReadOnlyList<string> options);

    Task ShowToastAsync(string message);

    /// <summary>Short message with an action button, e.g. "Agregada a la cola · Deshacer".</summary>
    Task ShowSnackbarAsync(string message, string actionText, Func<Task> action);

    /// <summary>
    /// Shows <paramref name="message"/> and lets the user copy <paramref name="report"/>
    /// (defaults to the message) to the clipboard.
    /// </summary>
    Task ShowErrorAsync(string title, string message, string? report = null);
}
