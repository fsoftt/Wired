namespace Wired.Core.Import;

public sealed class TrackImportedEventArgs(Guid localTrackId, ImportedTrack metadata) : EventArgs
{
    public Guid LocalTrackId { get; } = localTrackId;

    /// <summary>Title, artists and album as the imported file describes them (e.g. Spotify's).</summary>
    public ImportedTrack Metadata { get; } = metadata;
}
