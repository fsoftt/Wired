using CommunityToolkit.Mvvm.Messaging;
using Wired.App.Services;
using Wired.App.Views;
using Wired.Core.Abstractions;
using Wired.Core.Collections;
using Wired.Core.Browsing;
using Wired.Core.Downloads;
using Wired.Core.Enrichment;
using Wired.Core.Import;
using Wired.Core.Lyrics;
using Wired.Core.Playback;
using Wired.Core.Plugins;
using Wired.Core.Resilience;
using Wired.Core.Throttling;
using Wired.Core.UseCases;
using Wired.Infrastructure.Collections;
using Wired.Infrastructure.Home;
using Wired.Infrastructure.Library;
using Wired.Infrastructure.Media;
using Wired.Infrastructure.Lyrics;
using Wired.Presentation.Services;
using Wired.Presentation.ViewModels;

namespace Wired.App;

/// <summary>
/// Composition root: the only place that knows every layer.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Optional hook to register music sources that live outside this repository. It has no body here, so the call
    /// compiles away unless a <c>Wired.Plugins.props</c> (see <c>WiredPluginsProps</c> in Wired.App.csproj) adds a
    /// file that implements it.
    /// </summary>
    static partial void AddExternalMusicSources(IServiceCollection services);

    public static IServiceCollection AddMusicSources(this IServiceCollection services)
    {
        AddExternalMusicSources(services);

        return services
            .AddSingleton(new RequestThrottleRegistry(new ThrottleOptions()))
            .AddSingleton(new ResilienceOptions())
            // Every source is reached through its throttle and network resilience, whoever calls it.
            // Resilience is the outer layer so each retry also waits its turn in the throttle.
            .AddSingleton<IPluginCatalog>(sp =>
            {
                var throttles = sp.GetRequiredService<RequestThrottleRegistry>();
                var network = sp.GetRequiredService<INetworkMonitor>();
                var resilience = sp.GetRequiredService<ResilienceOptions>();
                return new PluginCatalog(throttles
                    .Wrap(sp.GetServices<IMusicSourcePlugin>())
                    .Select(source => NetworkResilientMusicSource.Wrap(source, network, resilience)));
            });
    }

    public static IServiceCollection AddDomain(this IServiceCollection services) =>
        services
            .AddSingleton<IClock, SystemClock>()
            .AddSingleton<PlaybackQueue>()
            .AddSingleton<IPlaybackSourceResolver, PlaybackSourceResolver>()
            .AddSingleton<IPlaybackService, PlaybackService>()
            .AddSingleton<PlaybackStatePersister>()
            .AddSingleton<SleepTimer>()
            .AddTransient<SearchTracksUseCase>()
            .AddTransient<GetHomeFeedUseCase>()
            .AddTransient<DownloadTrackUseCase>()
            .AddTransient<RemoveTrackUseCase>()
            .AddTransient<RecoverMusicUseCase>()
            .AddSingleton<RemoveDuplicateTracksUseCase>()
            .AddTransient<MoveMusicToSharedFolderUseCase>()
            .AddTransient<DownloadSharedLinkUseCase>()
            .AddSingleton<PlaylistService>()
            .AddSingleton<LikesService>()
            .AddSingleton<LyricsService>()
            .AddSingleton<CollectionsBackupService>()
            .AddSingleton<PlaylistImportService>()
            .AddSingleton<LibraryEnrichmentService>()
            .AddSingleton<PlayingTrackArtwork>()
            .AddSingleton<MediaBrowseTree>()
            .AddSingleton<IDownloadQueue>(sp => new DownloadQueue(sp.GetRequiredService<DownloadTrackUseCase>()));

    public static IServiceCollection AddPlatformServices(this IServiceCollection services) =>
        services
            .AddSingleton<IAppPaths, MauiAppPaths>()
            .AddSingleton<ITrackLibrary, JsonTrackLibrary>()
            .AddSingleton<IPlaylistRepository, JsonPlaylistRepository>()
            .AddSingleton<ILikedTracksRepository, JsonLikedTracksRepository>()
            .AddSingleton<IHomeFeedCache, FileHomeFeedCache>()
            .AddSingleton<ILyricsCache, FileLyricsCache>()
            .AddSingleton<IImportSessionRepository, JsonImportSessionRepository>()
            .AddSingleton<IPlaybackStateStore, JsonPlaybackStateStore>()
            .AddSingleton<IAudioTagger, TagLibAudioTagger>()
            .AddSingleton<IArtworkStore>(sp => new FileArtworkStore(sp.GetRequiredService<IAppPaths>(), new HttpClient { Timeout = TimeSpan.FromSeconds(20) }))
            .AddSingleton<ILyricsProvider>(_ => new LrcLibLyricsProvider(CreateLrcLibClient()))
#if ANDROID
            .AddSingleton<Core.Abstractions.IAudioPlayer, Playback.AndroidAudioPlayer>()
            .AddSingleton<ILoudnessAnalyzer, Playback.AndroidLoudnessAnalyzer>()
            .AddSingleton<IBackgroundPlayback, Playback.AndroidBackgroundPlayback>()
            .AddSingleton<IBackgroundDownloads, Downloads.AndroidBackgroundDownloads>()
#else
            .AddSingleton<Core.Abstractions.IAudioPlayer, MauiAudioPlayer>()
            .AddSingleton<IBackgroundPlayback, NoBackgroundPlayback>()
            .AddSingleton<IBackgroundDownloads, NoBackgroundDownloads>()
#endif
            .AddSingleton<INavigationService, ShellNavigationService>()
            .AddSingleton<IDialogService, MauiDialogService>()
            .AddSingleton<IMusicFolderAccess, MusicFolderAccess>()
            .AddSingleton<IPreferenceStore, MauiPreferenceStore>()
            .AddSingleton<INetworkMonitor, MauiNetworkMonitor>()
            .AddSingleton<IBackgroundExecution, BackgroundExecution>()
            .AddSingleton<ICsvFilePicker, MauiCsvFilePicker>()
            .AddSingleton<IUiDispatcher, MauiUiDispatcher>()
            .AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

    public static IServiceCollection AddPresentation(this IServiceCollection services) =>
        services
            .AddSingleton<LanguageSettings>()
            .AddSingleton<QueueActions>()
            .AddSingleton<SharedContentHandler>()
            .AddSingleton<TrackCollectionActions>()
            .AddSingleton<HomeViewModel>()
            .AddSingleton<SearchViewModel>()
            .AddSingleton<LibraryViewModel>()
            .AddSingleton<PlayerViewModel>()
            .AddSingleton<LyricsViewModel>()
            .AddSingleton<QueueViewModel>()
            .AddSingleton<DownloadsViewModel>()
            .AddSingleton<PlaylistsViewModel>()
            .AddTransient<PlaylistDetailViewModel>()
            .AddTransient<ImportProblemsViewModel>()
            .AddTransient<ImportProblemsPage>()
            .AddSingleton<HomePage>()
            .AddSingleton<SearchPage>()
            .AddSingleton<LibraryPage>()
            .AddSingleton<DownloadsPage>()
            .AddSingleton<PlaylistsPage>()
            .AddTransient<PlaylistDetailPage>()
            .AddTransient<NowPlayingPage>()
            .AddTransient<QueuePage>();

    /// <summary>LRCLIB asks clients to identify themselves with a User-Agent.</summary>
    private static HttpClient CreateLrcLibClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(LrcLibLyricsProvider.BaseUrl),
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Wired/1.0 (https://github.com/fsoftt/Wired)");
        return client;
    }
}
