namespace Wired.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// Wired is designed as a dark, neon themed app.
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell()) { Title = "Wired" };

		// The app may be killed while in background: keep the exact position of the queue.
		window.Stopped += (_, _) => _ = IPlatformApplication.Current?.Services.GetService<Core.Playback.PlaybackStatePersister>()?.SaveNowAsync();
		return window;
	}
}
