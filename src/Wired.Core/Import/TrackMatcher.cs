using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Wired.Core.Models;

namespace Wired.Core.Import;

/// <summary>
/// Picks, among search results of a music source, the one that is the same recording as an imported
/// track. Scores duration, title and artist, and penalizes other versions (live, covers, remixes...).
/// </summary>
public static partial class TrackMatcher
{
    public const int MinimumScore = 35;

    private static readonly string[] OtherVersionWords =
    [
        "live", "en vivo", "en directo", "cover", "karaoke", "remix", "sped up", "slowed", "reverb", "8d",
        "instrumental", "acoustic", "acustico", "reaction", "tutorial", "nightcore", "mashup", "bass boosted",
    ];

    /// <summary>Text to type in the source's search box.</summary>
    public static string SearchQuery(ImportedTrack track) => $"{track.MainArtist} {CleanTitle(track.Title)}".Trim();

    public static RemoteTrack? BestMatch(ImportedTrack track, IEnumerable<RemoteTrack> candidates) =>
        candidates
            .Select(c => (Candidate: c, Score: Score(track, c)))
            .Where(c => c.Score >= MinimumScore)
            .OrderByDescending(c => c.Score)
            .Select(c => c.Candidate)
            .FirstOrDefault();

    /// <summary>
    /// A track already downloaded that is the same recording, if any. Stricter than search results:
    /// the artist must match too, so a same-titled song by someone else is never reused.
    /// </summary>
    public static LocalTrack? BestLocalMatch(ImportedTrack track, IEnumerable<LocalTrack> library) =>
        library
            .Select(local => (Local: local, AsCandidate: new RemoteTrack(local.SourceId, local.RemoteId, local.Title, local.Artist, local.Duration, local.ThumbnailUrl)))
            .Where(c => ArtistMatches(track, c.AsCandidate))
            .Select(c => (c.Local, Score: Score(track, c.AsCandidate)))
            .Where(c => c.Score >= MinimumScore)
            .OrderByDescending(c => c.Score)
            .Select(c => c.Local)
            .FirstOrDefault();

    public static int Score(ImportedTrack track, RemoteTrack candidate)
    {
        var wantedTitle = Normalize(CleanTitle(track.Title));
        var candidateTitle = Normalize(candidate.Title);
        var candidateText = $"{candidateTitle} {Normalize(candidate.Artist)}";

        // The title must be there: everything else only ranks plausible results.
        var titleOverlap = Overlap(wantedTitle, candidateTitle);
        if (!candidateTitle.Contains(wantedTitle, StringComparison.Ordinal) && titleOverlap < 0.6)
            return 0;

        var score = candidateTitle.Contains(wantedTitle, StringComparison.Ordinal) ? 30 : (int)(titleOverlap * 25);

        if (ArtistMatches(track, candidate))
            score += 20;

        // Streaming catalogs' auto-generated "Artist - Topic" channels carry the official studio recording.
        if (candidate.Artist.EndsWith("- Topic", StringComparison.OrdinalIgnoreCase))
            score += 10;

        score += DurationScore(track.Duration, candidate.Duration);

        var wantedText = Normalize(track.Title);
        foreach (var word in OtherVersionWords)
        {
            if (ContainsWord(candidateText, word) && !ContainsWord(wantedText, word))
                score -= 25;
        }

        return score;
    }

    /// <summary>"Song - Remastered 2011" / "Song (feat. X)" → "Song".</summary>
    public static string CleanTitle(string title)
    {
        var clean = VersionSuffix().Replace(title, string.Empty);
        clean = Featuring().Replace(clean, string.Empty);
        return clean.Trim();
    }

    private static bool ArtistMatches(ImportedTrack track, RemoteTrack candidate)
    {
        var candidateText = $"{Normalize(candidate.Title)} {Normalize(candidate.Artist)}";
        return track.Artists.Any(a => Normalize(a) is { Length: > 0 } artist && candidateText.Contains(artist, StringComparison.Ordinal));
    }

    private static int DurationScore(TimeSpan? wanted, TimeSpan? actual)
    {
        if (wanted is null || actual is null)
            return 0;

        var gap = Math.Abs((wanted.Value - actual.Value).TotalSeconds);
        return gap switch
        {
            <= 3 => 30,
            <= 10 => 20,
            <= 30 => 5,
            <= 90 => -10,
            _ => -40,
        };
    }

    private static double Overlap(string wanted, string candidate)
    {
        var wantedWords = wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (wantedWords.Length == 0)
            return 0;

        var candidateWords = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        return wantedWords.Count(candidateWords.Contains) / (double)wantedWords.Length;
    }

    private static bool ContainsWord(string text, string word) =>
        $" {text} ".Contains($" {word} ", StringComparison.Ordinal);

    /// <summary>Lowercase, no accents, no punctuation, single spaces.</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return Spaces().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\s+-\s+.*\b(remaster(ed)?|version|versi[oó]n|mono|stereo|edit|mix|radio|single|deluxe|bonus)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffix();

    [GeneratedRegex(@"\s*[\(\[]\s*(feat\.?|ft\.?|with|con)\s+[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Featuring();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();
}
