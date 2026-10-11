namespace Wired.App.Services;

/// <summary>
/// Keeps downloads running while the app is not visible. On Android this needs a foreground
/// service: without it the system cuts the app's network access.
/// </summary>
public interface IBackgroundDownloads
{
    void Start();
}

public sealed class NoBackgroundDownloads : IBackgroundDownloads
{
    public void Start()
    {
    }
}
