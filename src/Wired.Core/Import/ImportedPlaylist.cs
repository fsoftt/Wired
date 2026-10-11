using System.Text.Json.Serialization;
namespace Wired.Core.Import;

/// <summary>A track described by another service (e.g. a Spotify export), not yet found anywhere.</summary>
public sealed record ImportedTrack(string Title, IReadOnlyList<string> Artists, string? Album, TimeSpan? Duration)
{
    [JsonIgnore]
    public string MainArtist => Artists.Count > 0 ? Artists[0] : string.Empty;

    [JsonIgnore]
    public string DisplayName => Artists.Count > 0 ? $"{string.Join(", ", Artists)} - {Title}" : Title;
}

public sealed record ImportedPlaylist(string Name, IReadOnlyList<ImportedTrack> Tracks)
{
    /// <summary>Rough size of the audio once downloaded (~128 kbps AAC).</summary>
    [JsonIgnore]
    public long EstimatedBytes => (long)Tracks.Sum(t => (t.Duration ?? TimeSpan.FromMinutes(3.5)).TotalSeconds * 16_000);
}
