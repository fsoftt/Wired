using Android.Content;
using Wired.App.Services;
using Wired.Core.Playback;

namespace Wired.App.Playback;

/// <summary>
/// Starts <see cref="MediaPlaybackService"/> the first time music starts, while the app is in the
/// foreground (Android forbids starting foreground services from the background).
/// The service stops itself when the queue is cleared.
/// </summary>
public sealed class AndroidBackgroundPlayback(IPlaybackService playback) : IBackgroundPlayback
{
    public void Start() => playback.StateChanged += OnPlaybackStateChanged;

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        if (!playback.IsPlaying || MediaPlaybackService.IsForeground)
            return;

        var context = Platform.AppContext;
        var intent = new Intent(context, typeof(MediaPlaybackService)).SetAction(MediaPlaybackService.ActionStart);

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }
        catch (Java.Lang.Exception)
        {
            // Started from the background (e.g. from Android Auto) where Android may refuse a
            // foreground service; the car connection keeps the app alive meanwhile.
        }
    }
}
