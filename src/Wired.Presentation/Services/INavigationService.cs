namespace Wired.Presentation.Services;

public interface INavigationService
{
    Task OpenNowPlayingAsync();

    /// <param name="playlistId">The playlist to open, or null for the "Me gusta" collection.</param>
    Task OpenPlaylistAsync(Guid? playlistId);

    /// <summary>The "A continuación" screen.</summary>
    Task OpenQueueAsync();

    /// <summary>The Downloads tab.</summary>
    Task OpenDownloadsAsync();

    /// <summary>Tracks of an import that were not found, to choose their version by hand.</summary>
    Task OpenImportProblemsAsync(Guid sessionId);

    Task GoBackAsync();
}
