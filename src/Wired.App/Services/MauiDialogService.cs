using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Wired.Core.Localization;
using Wired.Presentation.Services;

namespace Wired.App.Services;

public sealed class MauiDialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        Shell.Current.DisplayAlertAsync(title, message, accept, cancel);

    public Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? initialValue = null, int maxLength = -1) =>
        Shell.Current.DisplayPromptAsync(title, message, accept, cancel, maxLength: maxLength, initialValue: initialValue ?? string.Empty);

    public async Task<int?> ChooseAsync(string title, string cancel, IReadOnlyList<string> options)
    {
        var choice = await Shell.Current.DisplayActionSheetAsync(title, cancel, null, options.ToArray());
        if (choice is null || choice == cancel)
            return null;

        var index = options.ToList().IndexOf(choice);
        return index >= 0 ? index : null;
    }

    public async Task ShowErrorAsync(string title, string message, string? report = null)
    {
        var copy = await Shell.Current.DisplayAlertAsync(title, message, L.T("Common_Copy"), L.T("Common_Close"));
        if (!copy)
            return;

        await Clipboard.Default.SetTextAsync($"{title}\n{report ?? message}");
        await ShowToastAsync(L.T("Error_Copied"));
    }

    public async Task ShowSnackbarAsync(string message, string actionText, Func<Task> action)
    {
        try
        {
            await Snackbar.Make(message, () => _ = action(), actionText, TimeSpan.FromSeconds(4)).Show();
        }
        catch (Exception)
        {
            // Snackbars need extra setup on some targets (unpackaged Windows): fall back to a toast.
            await ShowToastAsync(message);
        }
    }

    public async Task ShowToastAsync(string message)
    {
        try
        {
            await Toast.Make(message, ToastDuration.Long).Show();
        }
        catch (Exception)
        {
            // Toasts are unavailable on some targets (e.g. unpackaged Windows apps).
            await Shell.Current.DisplayAlertAsync("Wired", message, "OK");
        }
    }
}
