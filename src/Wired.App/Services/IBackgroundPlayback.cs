namespace Wired.App.Services;

/// <summary>
/// Keeps music playing when the app is not visible and exposes system media controls
/// (notification, lock screen, headset buttons). Implemented per platform.
/// </summary>
public interface IBackgroundPlayback
{
    void Start();
}

/// <summary>Platforms where desktop apps keep playing when minimized need nothing extra.</summary>
public sealed class NoBackgroundPlayback : IBackgroundPlayback
{
    public void Start()
    {
    }
}
