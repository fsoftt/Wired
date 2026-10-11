using Android.Content;
using Wired.App.Services;
using Wired.Core.Downloads;

namespace Wired.App.Downloads;

/// <summary>Starts <see cref="DownloadForegroundService"/> whenever downloads are pending.</summary>
public sealed class AndroidBackgroundDownloads(IDownloadQueue queue) : IBackgroundDownloads
{
    public void Start()
    {
        queue.JobChanged += (_, _) => EnsureServiceRunning();
        EnsureServiceRunning();
    }

    private void EnsureServiceRunning()
    {
        if (DownloadForegroundService.IsRunning || !queue.Jobs.Any(j => j.IsActive))
            return;

        var context = Platform.AppContext;
        var intent = new Intent(context, typeof(DownloadForegroundService));
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }
        catch (Java.Lang.Exception)
        {
            // Android 12+ refuses to start foreground services from the background; the next
            // download started from the app (or reopening it) starts the service.
        }
    }
}
