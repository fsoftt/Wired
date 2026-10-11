using System.Globalization;
using System.Text;

namespace Wired.Presentation.Formatting;

/// <summary>
/// Alphabetical keys that ignore accents, case and leading symbols ("¡Hola", "Él" and "hola" sort
/// together), and the index letter of each: A-Z, or "#" for numbers and anything else.
/// </summary>
public static class SortKey
{
    public const string Other = "#";

    public static string For(string text)
    {
        var start = 0;
        while (start < text.Length && !char.IsLetterOrDigit(text[start]))
            start++;

        var builder = new StringBuilder(text.Length - start);
        foreach (var c in text[start..].Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>Index letter of a key from <see cref="For"/>.</summary>
    public static string LetterOf(string key) =>
        key.Length > 0 && key[0] is >= 'A' and <= 'Z' ? key[..1] : Other;
}
