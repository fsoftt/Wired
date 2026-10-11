namespace Wired.Presentation.Services;

/// <summary>Small user choices remembered between sessions (e.g. the library order).</summary>
public interface IPreferenceStore
{
    string? Get(string key);

    void Set(string key, string value);
}
