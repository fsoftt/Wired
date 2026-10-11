using Wired.App.Views;
using Wired.Presentation.Services;

namespace Wired.App.Services;

public sealed class ShellNavigationService : INavigationService
{
    public Task OpenNowPlayingAsync() => Shell.Current.GoToAsync(nameof(NowPlayingPage));

    public Task OpenPlaylistAsync(Guid? playlistId) =>
        Shell.Current.GoToAsync(nameof(PlaylistDetailPage), new ShellNavigationQueryParameters
        {
            [PlaylistDetailPage.PlaylistIdParameter] = playlistId?.ToString() ?? string.Empty,
        });

    public Task OpenQueueAsync() => Shell.Current.GoToAsync(nameof(QueuePage));

    public Task OpenDownloadsAsync() => Shell.Current.GoToAsync("//downloads");

    public Task OpenImportProblemsAsync(Guid sessionId) =>
        Shell.Current.GoToAsync(nameof(ImportProblemsPage), new ShellNavigationQueryParameters
        {
            [ImportProblemsPage.SessionIdParameter] = sessionId.ToString(),
        });

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");
}
