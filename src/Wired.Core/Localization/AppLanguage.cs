using System.Globalization;

namespace Wired.Core.Localization;

/// <summary>
/// The languages of the interface. The texts live in Strings.resx (Spanish, the neutral language), Strings.en.resx
/// and Strings.pt.resx. To add a language: copy Strings.resx to Strings.&lt;culture&gt;.resx, translate it and add its
/// code to <see cref="Supported"/>.
/// </summary>
public static class AppLanguage
{
    /// <summary>Preference key of the chosen language; empty or missing means "follow the device".</summary>
    public const string PreferenceKey = "app_language";

    /// <summary>Language used by devices that speak none of the <see cref="Supported"/> ones.</summary>
    public const string Fallback = "en";

    /// <summary>Codes of the available languages (the file culture of each Strings.xx.resx).</summary>
    public static readonly IReadOnlyList<string> Supported = ["es", "en", "pt"];

    /// <summary>Normalizes a stored code: one of <see cref="Supported"/>, or "" (automatic) when unknown.</summary>
    public static string Normalize(string? code)
    {
        code = (code ?? string.Empty).Trim().ToLowerInvariant();
        return Supported.Contains(code) ? code : string.Empty;
    }

    /// <summary>
    /// Culture to use for <paramref name="code"/>. Automatic follows the device (keeping its region for dates and
    /// numbers) when it speaks a supported language and falls back to <see cref="Fallback"/> otherwise.
    /// </summary>
    public static CultureInfo CultureFor(string? code, CultureInfo device)
    {
        code = Normalize(code);
        if (code.Length > 0)
            return CultureInfo.GetCultureInfo(code);

        return Supported.Contains(device.TwoLetterISOLanguageName) ? device : CultureInfo.GetCultureInfo(Fallback);
    }
}
