using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using Wired.Core.Downloads;
using DownloadStatus = Wired.Core.Downloads.DownloadStatus;
using Wired.Core.Localization;

namespace Wired.App.Downloads;

/// <summary>
/// Foreground service alive while downloads are pending. Android cuts network access for apps in
/// background ("Unable to resolve host"), so downloads need it to keep going with the app closed or the
/// screen off. Holds a CPU wake lock and a Wi-Fi lock and shows the progress in a notification.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class DownloadForegroundService : Service
{
    private const string ChannelId = "wired_downloads";
    private const int NotificationId = 1002;
    private static readonly TimeSpan MinUpdateInterval = TimeSpan.FromSeconds(1);

    private IDownloadQueue _queue = null!;
    private NotificationManager _notifications = null!;
    private PowerManager.WakeLock? _wakeLock;
    private WifiManager.WifiLock? _wifiLock;
    private DateTime _lastUpdate = DateTime.MinValue;

    public static bool IsRunning { get; private set; }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        IsRunning = true;

        _queue = IPlatformApplication.Current!.Services.GetRequiredService<IDownloadQueue>();
        _notifications = (NotificationManager)GetSystemService(NotificationService)!;
        CreateChannel();
        AcquireLocks();

        _queue.JobChanged += OnJobChanged;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var notification = BuildNotification();
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
        else
            StartForeground(NotificationId, notification);

        StopIfIdle();
        return StartCommandResult.NotSticky;
    }

    /// <summary>Android 15 limits data sync services to ~6 h a day; reopening the app continues.</summary>
    [SupportedOSPlatform("android35.0")]
    public override void OnTimeout(int startId, ForegroundService fgsType) => StopService();

    public override void OnDestroy()
    {
        IsRunning = false;
        _queue.JobChanged -= OnJobChanged;
        ReleaseLocks();
        base.OnDestroy();
    }

    private void OnJobChanged(object? sender, DownloadJobChangedEventArgs e)
    {
        if (StopIfIdle())
            return;

        // Progress events are frequent: refresh the notification at most once a second.
        var now = DateTime.UtcNow;
        if (now - _lastUpdate < MinUpdateInterval && e.Job.Status == DownloadStatus.Downloading)
            return;

        _lastUpdate = now;
        _notifications.Notify(NotificationId, BuildNotification());
    }

    private bool StopIfIdle()
    {
        if (_queue.Jobs.Any(j => j.IsActive))
            return false;

        StopService();
        return true;
    }

    private void StopService()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(24))
            StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    private Notification BuildNotification()
    {
        var jobs = _queue.Jobs;
        var pending = jobs.Count(j => j.IsActive);
        var current = jobs.FirstOrDefault(j => j.Status == DownloadStatus.Downloading);

        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, ChannelId)
#pragma warning disable CA1422 // Channels do not exist before Android 8.0
            : new Notification.Builder(this);
#pragma warning restore CA1422

        builder
            .SetSmallIcon(Resource.Drawable.ic_download)
            .SetContentTitle(L.N("Notif_Downloading", pending))
            .SetContentText(current?.Track.Title ?? L.T("Search_Queued"))
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetShowWhen(false)
            .SetContentIntent(OpenAppIntent());

        if (current is not null)
            builder.SetProgress(100, (int)(current.Progress * 100), current.Progress <= 0);

        return builder.Build();
    }

    private PendingIntent? OpenAppIntent()
    {
        var launch = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        return launch is null
            ? null
            : PendingIntent.GetActivity(this, 1, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
    }

    private void CreateChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var channel = new NotificationChannel(ChannelId, L.T("Tab_Downloads"), NotificationImportance.Low)
        {
            Description = L.T("Notif_DownloadsChannelDescription"),
        };
        channel.SetShowBadge(false);
        _notifications.CreateNotificationChannel(channel);
    }

    private void AcquireLocks()
    {
        var power = (PowerManager)GetSystemService(PowerService)!;
        _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "Wired:downloads");
        _wakeLock!.Acquire();

        var wifi = (WifiManager?)ApplicationContext?.GetSystemService(WifiService);
#pragma warning disable CA1422 // FullHighPerf is deprecated but still the lock that keeps Wi-Fi awake with the screen off.
        _wifiLock = wifi?.CreateWifiLock(Android.Net.WifiMode.FullHighPerf, "Wired:downloads");
#pragma warning restore CA1422
        _wifiLock?.Acquire();
    }

    private void ReleaseLocks()
    {
        if (_wakeLock?.IsHeld == true)
            _wakeLock.Release();
        if (_wifiLock?.IsHeld == true)
            _wifiLock.Release();
    }
}
