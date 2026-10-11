using Wired.Core.Models;

namespace Wired.Core.Playback;

/// <summary>
/// An entry of the play queue: a downloaded track or a remote track played by streaming.
/// </summary>
public sealed record PlaybackItem
{
    private PlaybackItem(Guid id, string title, string artist, TimeSpan? duration, string? thumbnailUrl, LocalTrack? localTrack, RemoteTrack? remoteTrack)
    {
        Id = id;
        Title = title;
        Artist = artist;
        Duration = duration;
        ThumbnailUrl = thumbnailUrl;
        LocalTrack = localTrack;
        RemoteTrack = remoteTrack;
    }

    /// <summary>Equals <see cref="Models.LocalTrack.Id"/> for downloaded tracks.</summary>
    public Guid Id { get; }

    public string Title { get; }

    public string Artist { get; }

    public TimeSpan? Duration { get; }

    public string? ThumbnailUrl { get; }

    /// <summary>Set when the track is on the device.</summary>
    public LocalTrack? LocalTrack { get; }

    /// <summary>Set when the track is streamed.</summary>
    public RemoteTrack? RemoteTrack { get; }

    public bool IsStream => LocalTrack is null;

    public static PlaybackItem FromLocal(LocalTrack track) =>
        new(track.Id, track.Title, track.Artist, track.Duration, track.Artwork, track, null);

    public static PlaybackItem FromStream(RemoteTrack track) =>
        new(Guid.NewGuid(), track.Title, track.Artist, track.Duration, track.ThumbnailUrl, null, track);
}
