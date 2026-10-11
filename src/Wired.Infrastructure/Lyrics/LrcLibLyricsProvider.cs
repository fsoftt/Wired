using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Wired.Core.Lyrics;

namespace Wired.Infrastructure.Lyrics;

/// <summary>
/// Lyrics from LRCLIB (https://lrclib.net): free, no API key, synchronized lyrics in LRC format.
/// Tries an exact lookup (title + artist + duration) and falls back to a ranked search.
/// </summary>
public sealed class LrcLibLyricsProvider(HttpClient http) : ILyricsProvider
{
    public const string BaseUrl = "https://lrclib.net/";

    /// <summary>Results whose duration differs more than this are probably another version.</summary>
    private static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(15);

    public async Task<Core.Lyrics.Lyrics?> FindAsync(LyricsQuery query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.Title))
            return null;

        var exact = await GetExactAsync(query, cancellationToken);
        if (exact is { HasLyrics: true })
            return exact.ToLyrics();

        var candidates = await SearchAsync($"api/search?track_name={Encode(query.Title)}&artist_name={Encode(query.Artist)}", cancellationToken);
        if (candidates.Count == 0)
            candidates = await SearchAsync($"api/search?q={Encode($"{query.Artist} {query.Title}")}", cancellationToken);

        return Rank(candidates, query)?.ToLyrics();
    }

    private async Task<LrcLibTrack?> GetExactAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        if (query.Duration is not { } duration || string.IsNullOrWhiteSpace(query.Artist))
            return null;

        var url = $"api/get?track_name={Encode(query.Title)}&artist_name={Encode(query.Artist)}&duration={(int)duration.TotalSeconds}";
        using var response = await http.GetAsync(url, cancellationToken);

        // 404 = no exact match. Any other client error (e.g. a parameter the API starts requiring)
        // must not prevent the fallback search.
        if (!response.IsSuccessStatusCode && (int)response.StatusCode < 500)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(LrcLibJsonContext.Default.LrcLibTrack, cancellationToken);
    }

    private async Task<IReadOnlyList<LrcLibTrack>> SearchAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(LrcLibJsonContext.Default.ListLrcLibTrack, cancellationToken) ?? [];
    }

    /// <summary>Synced lyrics first, then the closest duration; versions far off in length are discarded.</summary>
    private static LrcLibTrack? Rank(IReadOnlyList<LrcLibTrack> candidates, LyricsQuery query) =>
        candidates
            .Where(c => c.HasLyrics)
            .Select(c => (Track: c, DurationGap: DurationGap(c, query)))
            .Where(c => c.DurationGap <= DurationTolerance)
            .OrderByDescending(c => c.Track.SyncedLyrics is not null)
            .ThenBy(c => c.DurationGap)
            .Select(c => c.Track)
            .FirstOrDefault();

    private static TimeSpan DurationGap(LrcLibTrack track, LyricsQuery query) =>
        query.Duration is { } expected && track.Duration is { } actual
            ? (expected - TimeSpan.FromSeconds(actual)).Duration()
            : TimeSpan.Zero;

    private static string Encode(string value) => Uri.EscapeDataString(value);
}

internal sealed record LrcLibTrack(
    string? TrackName,
    string? ArtistName,
    double? Duration,
    bool Instrumental,
    string? PlainLyrics,
    string? SyncedLyrics)
{
    public bool HasLyrics => Instrumental || !string.IsNullOrWhiteSpace(SyncedLyrics) || !string.IsNullOrWhiteSpace(PlainLyrics);

    public Core.Lyrics.Lyrics ToLyrics() => new(
        string.IsNullOrWhiteSpace(SyncedLyrics) ? null : SyncedLyrics,
        string.IsNullOrWhiteSpace(PlainLyrics) ? null : PlainLyrics,
        Instrumental);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LrcLibTrack))]
[JsonSerializable(typeof(List<LrcLibTrack>))]
internal sealed partial class LrcLibJsonContext : JsonSerializerContext;
