using Wired.Core.Abstractions;
using Wired.Core.Models;
using Wired.Core.Storage;

namespace Wired.Core.UseCases;

/// <summary>
/// Adds to the library the audio files of the music folder that it does not know about,
/// e.g. after reinstalling the app. Metadata comes from the tags written into each file when
/// available, else from the file name.
/// </summary>
public sealed class RecoverMusicUseCase(
    ITrackLibrary library,
    IAppPaths paths,
    IAudioTagger? tagger = null,
    IArtworkStore? artwork = null)
{
    /// <returns>Number of tracks recovered.</returns>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(paths.MusicDirectory))
            return 0;

        var known = (await library.GetAllStoredAsync(cancellationToken))
            .Select(t => (t.SourceId, t.RemoteId))
            .ToHashSet();

        var recovered = 0;
        foreach (var path in Directory.EnumerateFiles(paths.MusicDirectory))
        {
            if (!MusicFileNaming.TryParse(Path.GetFileName(path), out var name) || known.Contains((name.SourceId, name.RemoteId)))
                continue;

            var tags = ReadTags(path);
            var artworkPath = tags?.Cover is { Length: > 0 } cover && artwork is not null
                ? await artwork.SaveAsync($"{name.SourceId}_{name.RemoteId}", cover, cancellationToken)
                : null;

            var track = new LocalTrack(
                Id: Guid.NewGuid(),
                Title: string.IsNullOrWhiteSpace(tags?.Title) ? name.Title : tags.Title,
                Artist: tags is { Artists.Count: > 0 } ? string.Join(", ", tags.Artists) : name.Artist,
                Duration: tags?.Duration,
                ThumbnailUrl: null,
                FilePath: path,
                SourceId: name.SourceId,
                RemoteId: name.RemoteId,
                AddedAt: File.GetLastWriteTime(path),
                Album: tags?.Album,
                ArtworkPath: artworkPath,
                IsEnriched: tags is not null);

            await library.AddAsync(track, cancellationToken);
            known.Add((name.SourceId, name.RemoteId));
            recovered++;
        }

        return recovered;
    }

    private AudioTags? ReadTags(string path)
    {
        try
        {
            return tagger?.Read(path);
        }
        catch (Exception)
        {
            return null; // Unreadable or untagged: the file name is enough.
        }
    }
}
