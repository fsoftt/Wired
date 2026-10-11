using Wired.Core.Abstractions;
using Wired.Core.Downloads;
using Wired.Core.Import;
using Wired.Core.Models;
using Wired.Core.Plugins;

namespace Wired.Core.Enrichment;

/// <summary>
/// Gives downloaded tracks their proper identity: saves the cover on the device (so it shows
/// offline), applies the real title/artists/album of imported tracks, and writes all of it into the
/// audio file's tags, and measures its loudness so every song plays at the same volume. Runs in
/// background for new downloads and, once, for the existing library.
/// </summary>
public sealed class LibraryEnrichmentService(
    ITrackLibrary library,
    IArtworkStore artwork,
    IAudioTagger tagger,
    IDownloadQueue downloads,
    PlaylistImportService imports,
    ILoudnessAnalyzer? loudness = null,
    IPluginCatalog? plugins = null)
{
    /// <summary>Small pause between tracks while backfilling, to stay light on battery and network.</summary>
    private static readonly TimeSpan BackfillPause = TimeSpan.FromMilliseconds(150);

    /// <summary>Problems kept for the report; older ones are dropped.</summary>
    private const int MaxProblems = 100;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _statusGate = new();
    private readonly List<string> _problems = [];
    private EnrichmentStatus _status = EnrichmentStatus.Idle;

    /// <summary>Progress of the backfill. Raised from any thread.</summary>
    public event EventHandler<EnrichmentStatus>? StatusChanged;

    public EnrichmentStatus Status
    {
        get
        {
            lock (_statusGate)
                return _status;
        }
    }

    /// <summary>Raised with the new version of a track whose metadata or cover changed. May be raised from any thread.</summary>
    public event EventHandler<LocalTrack>? TrackUpdated;

    /// <summary>Longest a single song may take to measure: a stuck decoder must not hold up the rest.</summary>
    private static readonly TimeSpan MeasureTimeout = TimeSpan.FromSeconds(30);

    public void Start()
    {
        downloads.JobChanged += (_, e) =>
        {
            if (e.Change == DownloadJobChange.Updated && e.Job is { Status: DownloadStatus.Completed, Result: { } track })
                Schedule(() => EnrichAsync(track.Id));
        };
        imports.TrackImported += (_, e) => Schedule(() => ApplyImportedAsync(e.LocalTrackId, e.Metadata));

        _ = Task.Run(async () =>
        {
            try
            {
                await BackfillAsync();
            }
            catch (Exception error)
            {
                // Retried on the next start.
                ReportProblem($"{error.GetType().Name}: {error.Message}");
                SetStatus(EnrichmentPhase.Idle, 0, 0);
            }
        });
    }

    /// <summary>
    /// Existing library: metadata of finished imports, then covers, tags and durations of every
    /// track (the newest first, as the library shows them), and only then the slower loudness
    /// measurement. Each track is its own unit of work, so new downloads do not wait for all of it.
    /// </summary>
    public async Task BackfillAsync()
    {
        SetStatus(EnrichmentPhase.Covers, 0, 0);
        foreach (var session in await imports.GetSessionsAsync())
        {
            foreach (var item in session.Items.Where(i => i is { Status: ImportItemStatus.Done, LocalTrackId: not null }))
                await RunExclusiveAsync(() => ApplyImportedCoreAsync(item.LocalTrackId!.Value, item.Track, measureLoudness: false));
        }

        var tracks = (await library.GetAllStoredAsync()).OrderByDescending(t => t.AddedAt).ToList();
        var missingCovers = tracks.Where(t => !HasCoverAndTags(t)).ToList();
        for (var i = 0; i < missingCovers.Count; i++)
        {
            SetStatus(EnrichmentPhase.Covers, i, missingCovers.Count);
            await RunExclusiveAsync(() => EnrichCoreAsync(missingCovers[i].Id, measureLoudness: false));
            await Task.Delay(BackfillPause);
        }

        if (loudness is not null)
        {
            var unmeasured = tracks.Where(t => t.LoudnessDb is null).ToList();
            for (var i = 0; i < unmeasured.Count; i++)
            {
                SetStatus(EnrichmentPhase.Loudness, i, unmeasured.Count);
                await RunExclusiveAsync(() => EnrichCoreAsync(unmeasured[i].Id, measureLoudness: true));
                await Task.Delay(BackfillPause);
            }
        }

        SetStatus(EnrichmentPhase.Idle, 0, 0);
    }

    public Task EnrichAsync(Guid trackId) => EnrichCoreAsync(trackId, measureLoudness: true);

    /// <summary>
    /// Gets the cover (and missing duration) of one track right away, e.g. the one starting to play,
    /// without waiting for its turn in the backfill. Tags are left for the backfill.
    /// </summary>
    /// <returns>The updated track, or null when nothing changed.</returns>
    public async Task<LocalTrack?> EnsureCoverAsync(Guid trackId)
    {
        try
        {
            if (await FindAsync(trackId) is not { } track || track.ArtworkPath is { } path && File.Exists(path))
                return null;

            var key = $"{track.SourceId}_{track.RemoteId}";
            var artworkPath = artwork.Find(key) ?? await DownloadCoverAsync(track, key);
            var thumbnail = track.ThumbnailUrl ?? ThumbnailFor(track);
            var duration = track.Duration ?? ReadDuration(track.FilePath);
            if (artworkPath == track.ArtworkPath && thumbnail == track.ThumbnailUrl && duration == track.Duration)
                return null;

            var latest = await FindAsync(trackId) ?? track;
            var updated = latest with { ArtworkPath = artworkPath ?? latest.ArtworkPath, ThumbnailUrl = latest.ThumbnailUrl ?? thumbnail, Duration = latest.Duration ?? duration };
            await library.UpdateAsync(updated);
            TrackUpdated?.Invoke(this, updated);
            return updated;
        }
        catch (Exception error)
        {
            ReportProblem($"{error.GetType().Name}: {error.Message}");
            return null;
        }
    }

    public Task ApplyImportedAsync(Guid trackId, ImportedTrack metadata) => ApplyImportedCoreAsync(trackId, metadata, measureLoudness: true);

    private async Task ApplyImportedCoreAsync(Guid trackId, ImportedTrack metadata, bool measureLoudness)
    {
        if (await FindAsync(trackId) is not { } track)
            return;

        var artist = string.Join(", ", metadata.Artists);
        if (track.Title == metadata.Title && track.Artist == artist && track.Album == metadata.Album)
            return;

        // Re-enrich so the file's tags carry the new names too.
        var renamed = track with { Title = metadata.Title, Artist = artist, Album = metadata.Album ?? track.Album, IsEnriched = false };
        await library.UpdateAsync(renamed);
        TrackUpdated?.Invoke(this, renamed);

        await EnrichCoreAsync(trackId, measureLoudness);
    }

    private async Task EnrichCoreAsync(Guid trackId, bool measureLoudness)
    {
        if (await FindAsync(trackId) is not { } track || !File.Exists(track.FilePath))
            return;

        var key = $"{track.SourceId}_{track.RemoteId}";
        var artworkPath = track.ArtworkPath is { } existing && File.Exists(existing)
            ? existing
            : artwork.Find(key) ?? await DownloadCoverAsync(track, key);

        if (!track.IsEnriched || artworkPath != track.ArtworkPath)
        {
            try
            {
                var cover = artworkPath is null ? null : await File.ReadAllBytesAsync(artworkPath);
                tagger.Write(track.FilePath, track.Title, SplitArtists(track.Artist), track.Album, cover);
            }
            catch (Exception)
            {
                // Files from a previous installation may be read-only for us; the library entry still improves.
            }
        }

        var enriched = track with
        {
            ArtworkPath = artworkPath,
            IsEnriched = true,
            LoudnessDb = track.LoudnessDb ?? (measureLoudness ? await MeasureLoudnessAsync(track.FilePath) : null),
            ThumbnailUrl = track.ThumbnailUrl ?? ThumbnailFor(track),
            Duration = track.Duration ?? ReadDuration(track.FilePath),
        };
        if (enriched == track)
            return;

        // Measuring takes a while: keep changes made meanwhile (e.g. the file moved), only set what we own.
        var latest = await FindAsync(trackId) ?? track;
        enriched = latest with
        {
            ArtworkPath = enriched.ArtworkPath,
            IsEnriched = true,
            LoudnessDb = enriched.LoudnessDb,
            ThumbnailUrl = enriched.ThumbnailUrl,
            Duration = enriched.Duration,
        };
        await library.UpdateAsync(enriched);
        TrackUpdated?.Invoke(this, enriched);
    }

    /// <summary>Enriched, with a duration, and its cover is on the device (unless there is none to fetch).</summary>
    private bool HasCoverAndTags(LocalTrack track) =>
        track.IsEnriched
        && track.Duration is not null
        && (track.ArtworkPath is { } path ? File.Exists(path) : ThumbnailFor(track) is null);

    private async Task<string?> DownloadCoverAsync(LocalTrack track, string key)
    {
        if (ThumbnailFor(track) is not { } url)
        {
            ReportProblem($"{track.Title}: la fuente \"{track.SourceId}\" no indica de dónde bajar la carátula");
            return null;
        }

        var path = await artwork.SaveFromUrlAsync(key, url);
        if (path is null)
            ReportProblem($"{track.Title}: no se pudo descargar la carátula ({url})");
        return path;
    }

    /// <summary>
    /// The cover URL saved with the download, or, for music recovered from the folder (only the
    /// file and its id are known), the one the source builds from the id.
    /// </summary>
    private string? ThumbnailFor(LocalTrack track)
    {
        if (track.ThumbnailUrl is not null)
            return track.ThumbnailUrl;

        try
        {
            return plugins?.Get(track.SourceId).ThumbnailUrlFor(track.RemoteId);
        }
        catch (PluginNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Recovered files without tags have no duration until it is read from the audio.</summary>
    private TimeSpan? ReadDuration(string filePath)
    {
        try
        {
            return tagger.ReadDuration(filePath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A file that cannot be measured gets the target level: it plays unchanged and is not
    /// decoded again on every start.
    /// </summary>
    private async Task<double?> MeasureLoudnessAsync(string filePath)
    {
        if (loudness is null)
            return null;

        try
        {
            using var timeout = new CancellationTokenSource(MeasureTimeout);
            return await loudness.MeasureAsync(filePath, timeout.Token) ?? Audio.LoudnessNormalization.TargetDb;
        }
        catch (Exception)
        {
            return Audio.LoudnessNormalization.TargetDb;
        }
    }

    private async Task<LocalTrack?> FindAsync(Guid trackId) =>
        (await library.GetAllStoredAsync()).FirstOrDefault(t => t.Id == trackId);

    private static IReadOnlyList<string> SplitArtists(string artist) =>
        artist.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Serialized background work; failures never surface (this is best effort).</summary>
    private void Schedule(Func<Task> work) => _ = Task.Run(() => RunExclusiveAsync(work));

    private async Task RunExclusiveAsync(Func<Task> work)
    {
        await _gate.WaitAsync();
        try
        {
            await work();
        }
        catch (Exception error)
        {
            // Retried on the next start by the backfill.
            ReportProblem($"{error.GetType().Name}: {error.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ReportProblem(string problem)
    {
        EnrichmentStatus status;
        lock (_statusGate)
        {
            _problems.Add(problem);
            if (_problems.Count > MaxProblems)
                _problems.RemoveAt(0);
            status = _status = _status with { Problems = [.. _problems] };
        }

        StatusChanged?.Invoke(this, status);
    }

    private void SetStatus(EnrichmentPhase phase, int done, int total)
    {
        EnrichmentStatus status;
        lock (_statusGate)
            status = _status = new EnrichmentStatus(phase, done, total, [.. _problems]);

        StatusChanged?.Invoke(this, status);
    }
}
