using Wired.Presentation.Services;

namespace Wired.App.Services;

public sealed class MauiPreferenceStore : IPreferenceStore
{
    public string? Get(string key) => Preferences.Default.Get<string?>(key, null);

    public void Set(string key, string value) => Preferences.Default.Set(key, value);
}
