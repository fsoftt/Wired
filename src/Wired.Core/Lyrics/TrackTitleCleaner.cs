using System.Text.RegularExpressions;

namespace Wired.Core.Lyrics;

/// <summary>
/// Turns video-style metadata ("Artist - Song (Official Video) [HD]" by "ArtistVEVO")
/// into the artist and title a lyrics database knows.
/// </summary>
public static partial class TrackTitleCleaner
{
    public static (string Artist, string Title) Clean(string rawTitle, string rawArtist)
    {
        var artist = CleanArtist(rawArtist);
        var title = rawTitle;

        // "Song | Album | Label" → "Song"
        var pipe = title.IndexOf('|');
        if (pipe > 0)
            title = title[..pipe];

        title = NoiseInBrackets().Replace(title, " ");
        title = Featuring().Replace(title, " ");

        // "Artist - Song" → artist from the title is more reliable than the channel name.
        var separator = ArtistSeparator().Match(title);
        if (separator.Success)
        {
            artist = title[..separator.Index].Trim();
            title = title[(separator.Index + separator.Length)..];
        }

        return (Normalize(artist), Normalize(title.Trim('"', '\'', '“', '”', ' ')));
    }

    private static string CleanArtist(string channel)
    {
        var artist = TopicSuffix().Replace(channel, string.Empty);
        artist = ChannelNoise().Replace(artist, string.Empty);
        return artist.Trim();
    }

    private static string Normalize(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s*[\(\[\{][^\)\]\}]*\b(official|oficial|video|v[ií]deo|audio|lyrics?|letra|visuali[sz]er|hd|hq|4k|mv|clip|explicit|remaster(ed)?)\b[^\)\]\}]*[\)\]\}]", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseInBrackets();

    [GeneratedRegex(@"\s*[\(\[]?\s*\b(ft\.?|feat\.?|featuring)\s+[^\)\]\-]*[\)\]]?", RegexOptions.IgnoreCase)]
    private static partial Regex Featuring();

    [GeneratedRegex(@"\s+[-–—]\s+")]
    private static partial Regex ArtistSeparator();

    [GeneratedRegex(@"\s*-\s*topic$", RegexOptions.IgnoreCase)]
    private static partial Regex TopicSuffix();

    [GeneratedRegex(@"(VEVO|\s+(official|oficial)(\s+(channel|canal))?)$", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelNoise();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Whitespace();
}
