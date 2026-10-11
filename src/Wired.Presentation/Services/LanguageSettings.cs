using CommunityToolkit.Mvvm.Messaging;
using Wired.Core.Localization;
using Wired.Presentation.Messages;

namespace Wired.Presentation.Services;

/// <summary>
/// The language of the interface: automatic (the device's) or one of <see cref="AppLanguage.Supported"/>. The choice
/// is remembered and applied right away; screens read their texts again when it changes.
/// </summary>
public sealed class LanguageSettings(IPreferenceStore preferences, IDialogService dialogs)
{
    private static readonly (string Code, string LabelKey)[] Options =
    [
        (string.Empty, "Language_Auto"),
        ("es", "Language_Spanish"),
        ("en", "Language_English"),
        ("pt", "Language_Portuguese"),
    ];

    /// <summary>Applies the saved language. Call it before any text is shown.</summary>
    public static void ApplySaved(IPreferenceStore preferences) => L.Apply(preferences.Get(AppLanguage.PreferenceKey));

    /// <summary>Asks which language to use and applies it.</summary>
    public async Task ChooseAsync()
    {
        var labels = Options.Select(o => (o.Code == L.Language ? "✓ " : string.Empty) + L.T(o.LabelKey)).ToList();
        if (await dialogs.ChooseAsync(L.T("Language_Title"), L.T("Common_Cancel"), labels) is { } index)
            Set(Options[index].Code);
    }

    public void Set(string code)
    {
        code = AppLanguage.Normalize(code);
        if (code == L.Language)
            return;

        preferences.Set(AppLanguage.PreferenceKey, code);
        L.Apply(code);
        WeakReferenceMessenger.Default.Send(new LanguageChangedMessage());
    }
}
