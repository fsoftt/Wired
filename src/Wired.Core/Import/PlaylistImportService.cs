using Wired.Core.Abstractions;
using Wired.Core.Collections;
using Wired.Core.Downloads;
using Wired.Core.Localization;
using Wired.Core.Models;
using Wired.Core.Throttling;
using Wired.Core.UseCases;

namespace Wired.Core.Import;

/// <summary>
/// Finds every track of an imported playlist on a music source, downloads it through the
/// <see cref="IDownloadQueue"/> and fills a local playlist and/or the likes, keeping the file's order.
/// Imports survive app restarts: <see cref="ResumeAsync"/> continues them.
/// </summary>
public sealed class PlaylistImportService
{
    /// <summary>Parallel searches; downloads have their own limit in the queue.</summary>
    private const int ParallelSearches = 2;

    private readonly IImportSessionRepository _sessions;
    private readonly SearchTracksUseCase _search;
    private readonly IDownloadQueue _downloads;
    private readonly PlaylistService _playlists;
    private readonly LikesService _likes;
    private readonly IClock _clock;
    private readonly ITrackLibrary _library;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _collectionsGate = new(1, 1);
    private readonly Dictionary<Guid, CancellationTokenSource> _running = [];
    private bool _resumed;

    public PlaylistImportService(
        IImportSessionRepository sessions,
        SearchTracksUseCase search,
        IDownloadQueue downloads,
        PlaylistService playlists,
        LikesService likes,
        ITrackLibrary library,
        IClock clock)
    {
        _library = library;
        _sessions = sessions;
        _search = search;
        _downloads = downloads;
        _playlists = playlists;
        _likes = likes;
        _clock = clock;

        _downloads.JobChanged += OnDownloadJobChanged;
    }

    /// <summary>
    /// Raised when an imported track is in the library (downloaded or already there), with the
    /// metadata from the imported file. May be raised from any thread.
    /// </summary>
    public event EventHandler<TrackImportedEventArgs>? TrackImported;

    /// <summary>Raised when a session starts, progresses, finishes or is removed. May be raised from any thread.</summary>
    public event EventHandler<PlaylistImportSession>? SessionChanged;

    public Task<IReadOnlyList<PlaylistImportSession>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        _sessions.GetAllAsync(cancellationToken);

    public async Task<PlaylistImportSession> StartAsync(ImportedPlaylist playlist, ImportOptions options, CancellationToken cancellationToken = default)
    {
        Guid? playlistId = options.CreatePlaylist
            ? (await _playlists.CreateAsync(playlist.Name, cancellationToken)).Id
            : null;

        var session = new PlaylistImportSession(
            Guid.NewGuid(),
            playlist.Name,
            options.SourceId,
            playlistId,
            options.AddToLikes,
            _clock.Now,
            playlist.Tracks.Select((track, index) => new ImportItem(index, track)).ToList());

        await SaveAsync(session);
        Run(session);
        return session;
    }

    /// <summary>Continues imports interrupted by closing the app. Safe to call more than once.</summary>
    public async Task ResumeAsync()
    {
        if (_resumed)
            return;
        _resumed = true;

        foreach (var session in await _sessions.GetAllAsync())
        {
            if (session.IsRunning)
                Run(session);
        }
    }

    public async Task CancelAsync(Guid sessionId)
    {
        CancellationTokenSource? cts;
        lock (_running)
            _running.Remove(sessionId, out cts);
        cts?.Cancel();

        await UpdateAsync(sessionId, session =>
        {
            foreach (var item in session.Items.Where(i => i is { Status: ImportItemStatus.Downloading, Match: not null }))
            {
                if (_downloads.FindActive(item.Match!.SourceId, item.Match.RemoteId) is { } job)
                    _downloads.Cancel(job);
            }

            return session with
            {
                Items = session.Items.Select(i => i.IsFinished ? i : i with { Status = ImportItemStatus.Canceled }).ToList(),
            };
        });
    }

    /// <summary>
    /// The user picked the version of a track that was not found (or failed): download it and
    /// put it in its place in the playlist, like any other imported track.
    /// </summary>
    public async Task ChooseMatchAsync(Guid sessionId, int itemIndex, RemoteTrack match)
    {
        var updated = await UpdateAsync(sessionId, session => session with
        {
            Items = session.Items
                .Select(i => i.Index == itemIndex ? i with { Status = ImportItemStatus.Downloading, Match = match, Error = null } : i)
                .ToList(),
        });
        if (updated is null)
            return;

        var job = _downloads.Enqueue(match);

        // Already in the library: the job completes before we could observe it.
        if (job is { Status: DownloadStatus.Completed, Result: { } local })
            await CompleteAsync(match, local);
    }

    /// <summary>Forgets a finished import (the playlist and tracks stay).</summary>
    public async Task DismissAsync(Guid sessionId)
    {
        var session = (await _sessions.GetAllAsync()).FirstOrDefault(s => s.Id == sessionId);
        if (session is null || session.IsRunning)
            return;

        await _sessions.DeleteAsync(sessionId);
        SessionChanged?.Invoke(this, session with { Items = [] });
    }

    private void Run(PlaylistImportSession session)
    {
        var cts = new CancellationTokenSource();
        lock (_running)
            _running[session.Id] = cts;

        _ = Task.Run(() => RunAsync(session, cts.Token));
    }

    private async Task RunAsync(PlaylistImportSession session, CancellationToken cancellationToken)
    {
        // Imports can mean hundreds of requests: pace them so the source does not block us,
        // without slowing down what the user does meanwhile.
        using var priority = RequestPriority.Background();

        // Downloads interrupted by an app restart are queued again; finished ones are deduplicated
        // by the download use case.
        foreach (var item in session.Items.Where(i => i is { Status: ImportItemStatus.Downloading, Match: not null }))
            _downloads.Enqueue(item.Match!);

        var pending = session.Items.Where(i => i.Status == ImportItemStatus.Pending).ToList();
        if (pending.Count == 0)
            return;

        // Tracks already downloaded (by an earlier import, a search, a recovered folder...) need no request.
        var library = await _library.GetAllAsync(cancellationToken);
        var toSearch = new List<ImportItem>();
        foreach (var item in pending)
        {
            if (TrackMatcher.BestLocalMatch(item.Track, library) is { } local)
                await MarkDoneAsync(session.Id, item with { AlreadyInLibrary = true }, local);
            else
                toSearch.Add(item);
        }

        await Parallel.ForEachAsync(
            toSearch,
            new ParallelOptions { MaxDegreeOfParallelism = ParallelSearches, CancellationToken = cancellationToken },
            async (item, ct) => await ProcessAsync(session.Id, session.SourceId, item, ct))
            .ContinueWith(_ => { }, TaskScheduler.Default); // Cancellation ends the loop quietly.
    }

    private async Task ProcessAsync(Guid sessionId, string sourceId, ImportItem item, CancellationToken cancellationToken)
    {
        ImportItem updated;
        try
        {
            var results = await _search.ExecuteAsync(sourceId, TrackMatcher.SearchQuery(item.Track), cancellationToken);
            var match = TrackMatcher.BestMatch(item.Track, results);

            updated = match is null
                ? item with { Status = ImportItemStatus.NotFound }
                : item with { Status = ImportItemStatus.Downloading, Match = match };
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception error)
        {
            updated = item with { Status = ImportItemStatus.Failed, Error = error.Message };
        }

        await UpdateItemAsync(sessionId, updated);

        if (updated is { Status: ImportItemStatus.Downloading, Match: { } remote })
        {
            var job = _downloads.Enqueue(remote);

            // Already in the library: the job completes before we could observe it.
            if (job is { Status: DownloadStatus.Completed, Result: { } local })
                await CompleteAsync(remote, local);
        }
    }

    private void OnDownloadJobChanged(object? sender, DownloadJobChangedEventArgs e)
    {
        if (e.Change != DownloadJobChange.Updated)
            return;

        var job = e.Job;
        switch (job.Status)
        {
            case DownloadStatus.Completed when job.Result is { } local:
                _ = CompleteAsync(job.Track, local);
                break;
            case DownloadStatus.Failed or DownloadStatus.Canceled:
                _ = FailAsync(job.Track, job.Status == DownloadStatus.Failed ? job.Error : L.T("Import_Canceled"));
                break;
        }
    }

    private async Task CompleteAsync(RemoteTrack remote, LocalTrack local)
    {
        foreach (var session in await SessionsWaitingFor(remote))
        {
            // The same song can appear more than once in a file: one download completes every row.
            foreach (var item in session.Items.Where(i => i.Status == ImportItemStatus.Downloading && i.Match == remote))
                await MarkDoneAsync(session.Id, item, local);
        }
    }

    /// <summary>Marks the item done and adds the track to the import's playlist and/or likes.</summary>
    private async Task MarkDoneAsync(Guid sessionId, ImportItem item, LocalTrack local)
    {
        var updated = await UpdateItemAsync(sessionId, item with { Status = ImportItemStatus.Done, LocalTrackId = local.Id });
        if (updated is null)
            return;

        TrackImported?.Invoke(this, new TrackImportedEventArgs(local.Id, item.Track));

        // Serialized so concurrent completions never overwrite each other's playlist order.
        await _collectionsGate.WaitAsync();
        try
        {
            if (updated.AddToLikes)
                await _likes.SetLikedAsync(local.Id, liked: true);

            if (updated.PlaylistId is { } playlistId)
            {
                var latest = (await _sessions.GetAllAsync()).FirstOrDefault(s => s.Id == updated.Id) ?? updated;
                await SyncPlaylistAsync(latest, playlistId);
            }
        }
        finally
        {
            _collectionsGate.Release();
        }
    }

    private async Task FailAsync(RemoteTrack remote, string? error)
    {
        foreach (var session in await SessionsWaitingFor(remote))
        {
            foreach (var item in session.Items.Where(i => i.Status == ImportItemStatus.Downloading && i.Match == remote))
                await UpdateItemAsync(session.Id, item with { Status = ImportItemStatus.Failed, Error = error });
        }
    }

    /// <summary>Imported tracks first, in the file's order; tracks the user added by hand keep their place after them.</summary>
    private async Task SyncPlaylistAsync(PlaylistImportSession session, Guid playlistId)
    {
        var playlist = await _playlists.FindAsync(playlistId);
        if (playlist is null)
            return; // Deleted by the user.

        var imported = session.Items
            .Where(i => i is { Status: ImportItemStatus.Done, LocalTrackId: not null })
            .OrderBy(i => i.Index)
            .Select(i => i.LocalTrackId!.Value)
            .Distinct()
            .ToList();
        var importedSet = imported.ToHashSet();
        var others = playlist.TrackIds.Where(id => !importedSet.Contains(id));

        await _playlists.SetTracksAsync(playlist, [.. imported, .. others]);
    }

    private async Task<IReadOnlyList<PlaylistImportSession>> SessionsWaitingFor(RemoteTrack remote) =>
        (await _sessions.GetAllAsync())
            .Where(s => s.Items.Any(i => i.Status == ImportItemStatus.Downloading && i.Match == remote))
            .ToList();

    private Task<PlaylistImportSession?> UpdateItemAsync(Guid sessionId, ImportItem item) =>
        UpdateAsync(sessionId, session => session with
        {
            Items = session.Items.Select(i => i.Index == item.Index ? item : i).ToList(),
        });

    /// <summary>Read-modify-write under a lock: searches and downloads finish concurrently.</summary>
    private async Task<PlaylistImportSession?> UpdateAsync(Guid sessionId, Func<PlaylistImportSession, PlaylistImportSession> change)
    {
        PlaylistImportSession? updated;
        await _gate.WaitAsync();
        try
        {
            var current = (await _sessions.GetAllAsync()).FirstOrDefault(s => s.Id == sessionId);
            if (current is null)
                return null;

            updated = change(current);
            await _sessions.SaveAsync(updated);
        }
        finally
        {
            _gate.Release();
        }

        SessionChanged?.Invoke(this, updated);
        return updated;
    }

    private async Task SaveAsync(PlaylistImportSession session)
    {
        await _sessions.SaveAsync(session);
        SessionChanged?.Invoke(this, session);
    }
}
