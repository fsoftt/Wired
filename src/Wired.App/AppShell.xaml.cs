using Wired.App.Views;
using Wired.Presentation.ViewModels;

namespace Wired.App;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(NowPlayingPage), typeof(NowPlayingPage));
		Routing.RegisterRoute(nameof(PlaylistDetailPage), typeof(PlaylistDetailPage));
		Routing.RegisterRoute(nameof(QueuePage), typeof(QueuePage));
		Routing.RegisterRoute(nameof(ImportProblemsPage), typeof(ImportProblemsPage));
	}

	private bool _startupChecksDone;

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (_startupChecksDone)
			return;
		_startupChecksDone = true;

		try
		{
			// Let the first page render before showing any dialog.
			await Task.Delay(TimeSpan.FromSeconds(1));
			var library = Handler?.MauiContext?.Services.GetService<LibraryViewModel>();
			if (library is not null)
				await library.OfferRecoveryIfNeededAsync();
		}
		catch (Exception)
		{
			// A startup suggestion must never crash the app; the Library tab offers recovery too.
		}
	}
}
