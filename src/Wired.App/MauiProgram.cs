using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using Wired.App.Localization;
using Wired.App.Services;
using Wired.Core.Collections;
using Wired.Core.Enrichment;
using Wired.Core.Import;
using Wired.Core.Playback;
using Wired.Core.UseCases;
using Wired.Presentation.ViewModels;

namespace Wired.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		// Before any text is built: the screens read their labels in the language already chosen.
		LocalizationManager.Initialize();

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.AddAudio(ConfigurePlayback)
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services
			.AddMusicSources()
			.AddDomain()
			.AddPlatformServices()
			.AddPresentation();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();
		StartBackgroundServices(app.Services);
		return app;
	}

	private static void ConfigurePlayback(AudioPlayerOptions options)
	{
#if IOS || MACCATALYST
		// "Playback" keeps audio running with the screen locked or the app in background.
		options.Category = AVFoundation.AVAudioSessionCategory.Playback;
#endif
	}

	/// <summary>Services that must live for the whole app, not only while their screen is open.</summary>
	private static void StartBackgroundServices(IServiceProvider services)
	{
		services.GetRequiredService<IBackgroundPlayback>().Start();
		services.GetRequiredService<IBackgroundDownloads>().Start();
		_ = services.GetRequiredService<LikesService>().InitializeAsync();

		// Older versions kept music in private storage (wiped on uninstall): move it to the shared folder.
		// Then mirror playlists and likes next to it (Music/Wired/Playlists) so they survive too.
		var removeDuplicates = services.GetRequiredService<RemoveDuplicateTracksUseCase>();
		var moveMusic = services.GetRequiredService<MoveMusicToSharedFolderUseCase>();
		var collectionsBackup = services.GetRequiredService<CollectionsBackupService>();
		var enrichment = services.GetRequiredService<LibraryEnrichmentService>();

		// The song that starts playing gets its cover right away.
		services.GetRequiredService<PlayingTrackArtwork>().Start();
		_ = Task.Run(async () =>
		{
			try
			{
				await removeDuplicates.ExecuteAsync();
				await moveMusic.ExecuteAsync();
			}
			finally
			{
				collectionsBackup.Start();

				// Covers and tags for new downloads, and once for tracks downloaded before.
				enrichment.Start();
			}
		});

		// The queue of the last session comes back paused, ready to continue.
		var playbackState = services.GetRequiredService<PlaybackStatePersister>();
		_ = Task.Run(async () =>
		{
			await removeDuplicates.ExecuteAsync();
			await playbackState.StartAsync();
		});

		// Playlist imports interrupted by closing the app continue.
		var imports = services.GetRequiredService<PlaylistImportService>();
		_ = Task.Run(imports.ResumeAsync);

		// Reports failed downloads even if the Downloads tab was never opened.
		services.GetRequiredService<DownloadsViewModel>();
	}
}
