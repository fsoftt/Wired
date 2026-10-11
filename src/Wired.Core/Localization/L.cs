using System.Globalization;
using System.Resources;

[assembly: NeutralResourcesLanguage("es")]

namespace Wired.Core.Localization;

/// <summary>
/// Texts of the interface in the current language: <c>L.T("Key")</c>, <c>L.T("Key", arg)</c> or, for counts,
/// <c>L.N("Songs", count)</c> (uses Songs_One / Songs_Other). A missing key shows the key itself.
/// </summary>
public static class L
{
    private static readonly ResourceManager Resources = new("Wired.Core.Localization.Strings", typeof(L).Assembly);

    private static readonly CultureInfo Device = CultureInfo.CurrentUICulture;

    /// <summary>Code chosen by the user: "" (automatic), "es", "en" or "pt".</summary>
    public static string Language { get; private set; } = string.Empty;

    /// <summary>Applies a language to the whole process, so formatting and plugin resources follow it too.</summary>
    public static void Apply(string? code)
    {
        Language = AppLanguage.Normalize(code);
        var culture = AppLanguage.CultureFor(Language, Device);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static string T(string key)
    {
        try
        {
            return Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }
        catch (MissingManifestResourceException)
        {
            return key;
        }
    }

    public static string T(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>"1 song" / "5 songs": <paramref name="name"/>_One when <paramref name="count"/> is 1, else name_Other.</summary>
    public static string N(string name, int count) => T(count == 1 ? $"{name}_One" : $"{name}_Other", count);
}
