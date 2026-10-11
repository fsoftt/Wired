using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Media;
using Android.Media.Browse;
using Android.Media.Session;
using Android.OS;
using Android.Service.Media;
using Wired.Core.Browsing;
using Wired.Core.Localization;
using Wired.Core.Playback;

namespace Wired.App.Playback;

/// <summary>
/// Foreground service that keeps the process alive while music is loaded and publishes a media
/// notification plus a <see cref="MediaSession"/> (lock screen, Bluetooth and headset controls).
/// It is also a <see cref="MediaBrowserService"/>: Android Auto (and other media browsers) bind to it
/// to browse the library, see the queue and start playback by id or by voice search.
/// The audio itself is played by <see cref="AndroidAudioPlayer"/> through <see cref="IPlaybackService"/>.
/// </summary>
[Service(Exported = true, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
[IntentFilter(["android.media.browse.MediaBrowserService"])]
public sealed class MediaPlaybackService : MediaBrowserService
{
    /// <summary>Queue entries published to Android Auto around the current one.</summary>
    private const int QueueWindow = 100;

    public const string ActionStart = "com.wired.player.action.START";
    private const string ActionPlayPause = "com.wired.player.action.PLAY_PAUSE";
    private const string ActionNext = "com.wired.player.action.NEXT";
    private const string ActionPrevious = "com.wired.player.action.PREVIOUS";

    private const string ChannelId = "wired_playback";
    private const int NotificationId = 1001;

    private static readonly HttpClient Http = new();

    private IPlaybackService _playback = null!;
    private MediaBrowseTree _browseTree = null!;
    private MediaSession _session = null!;
    private NotificationManager _notifications = null!;

    private string? _artworkUrl;
    private Bitmap? _artwork;
    private string? _publishedQueueKey;

    public static bool IsRunning { get; private set; }

    /// <summary>
    /// True once promoted to a foreground service. Android Auto may create the service only to
    /// browse, so existing (<see cref="IsRunning"/>) does not mean it keeps the app alive.
    /// </summary>
    public static bool IsForeground { get; private set; }

    public override void OnCreate()
    {
        base.OnCreate();
        IsRunning = true;

        var services = IPlatformApplication.Current!.Services;
        _playback = services.GetRequiredService<IPlaybackService>();
        _browseTree = services.GetRequiredService<MediaBrowseTree>();
        _notifications = (NotificationManager)GetSystemService(NotificationService)!;
        CreateNotificationChannel();

        _session = new MediaSession(this, "Wired");
        _session.SetCallback(new SessionCallback(_playback, _browseTree));
        _session.SetQueueTitle(L.T("Queue_Description"));
        _session.Active = true;
        SessionToken = _session.SessionToken;

        _playback.StateChanged += OnPlaybackStateChanged;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        // Must be called promptly after startForegroundService().
        var notification = BuildNotification();
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
        else
            StartForeground(NotificationId, notification);
        IsForeground = true;

        switch (intent?.Action)
        {
            case ActionPlayPause:
                _playback.TogglePlayPause();
                break;
            case ActionNext:
                _ = _playback.NextAsync();
                break;
            case ActionPrevious:
                _ = _playback.PreviousAsync();
                break;
        }

        return StartCommandResult.NotSticky;
    }

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        // The user swiped the app away: keep playing if music is on, otherwise go away.
        if (!_playback.IsPlaying)
            StopSelf();

        base.OnTaskRemoved(rootIntent);
    }

    /// <summary>Android Auto and other browsers start here.</summary>
    public override BrowserRoot? OnGetRoot(string clientPackageName, int clientUid, Bundle? rootHints)
    {
        var extras = new Bundle();
        // Lists of tracks read better than grids in the car.
        extras.PutInt("android.media.browse.CONTENT_STYLE_BROWSABLE_HINT", 1);
        extras.PutInt("android.media.browse.CONTENT_STYLE_PLAYABLE_HINT", 1);
        extras.PutBoolean("android.media.browse.SEARCH_SUPPORTED", true);
        return new BrowserRoot(MediaBrowseTree.RootId, extras);
    }

    public override void OnLoadChildren(string parentId, Result result)
    {
        // Loading reads files: answer asynchronously.
        result.Detach();
        _ = Task.Run(async () =>
        {
            try
            {
                var nodes = await _browseTree.GetChildrenAsync(parentId);
                result.SendResult(new Android.Runtime.JavaList<MediaBrowser.MediaItem>(nodes.Select(ToMediaItem)));
            }
            catch (Exception)
            {
                result.SendResult(new Android.Runtime.JavaList<MediaBrowser.MediaItem>());
            }
        });
    }

    public override void OnDestroy()
    {
        IsRunning = false;
        IsForeground = false;
        _playback.StateChanged -= OnPlaybackStateChanged;
        _session.Active = false;
        _session.Release();
        base.OnDestroy();
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        if (_playback.CurrentItem is null)
        {
            StopForegroundCompat();
            StopSelf();
            return;
        }

        _notifications.Notify(NotificationId, BuildNotification());
        PublishQueue();
    }

    private Notification BuildNotification()
    {
        var item = _playback.CurrentItem;
        UpdateSession(item);

        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, ChannelId)
#pragma warning disable CA1422 // Channels do not exist before Android 8.0
            : new Notification.Builder(this);
#pragma warning restore CA1422

        var playPauseIcon = _playback.IsPlaying ? Resource.Drawable.ic_pause : Resource.Drawable.ic_play;

        builder
            .SetSmallIcon(Resource.Drawable.ic_music_note)
            .SetContentTitle(item?.Title ?? "Wired")
            .SetContentText(item?.Artist ?? string.Empty)
            .SetLargeIcon(_artwork)
            .SetContentIntent(OpenAppIntent())
            .SetVisibility(NotificationVisibility.Public)
            .SetOngoing(_playback.IsPlaying)
            .SetShowWhen(false)
            .AddAction(CreateAction(Resource.Drawable.ic_previous, L.T("Notif_Previous"), ActionPrevious))
            .AddAction(CreateAction(playPauseIcon, L.T(_playback.IsPlaying ? "Notif_Pause" : "Common_Play"), ActionPlayPause))
            .AddAction(CreateAction(Resource.Drawable.ic_next, L.T("Notif_Next"), ActionNext))
            .SetStyle(new Notification.MediaStyle()
                .SetMediaSession(_session.SessionToken)!
                .SetShowActionsInCompactView(0, 1, 2));

        return builder.Build();
    }

    private void UpdateSession(PlaybackItem? track)
    {
        if (track is not null)
        {
            LoadArtworkIfNeeded(track.ThumbnailUrl);

            var metadata = new MediaMetadata.Builder()
                .PutString(MediaMetadata.MetadataKeyTitle, track.Title)!
                .PutString(MediaMetadata.MetadataKeyArtist, track.Artist)!
                .PutLong(MediaMetadata.MetadataKeyDuration, (long)_playback.Duration.TotalMilliseconds)!;
            if (_artwork is not null)
                metadata.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, _artwork);

            _session.SetMetadata(metadata.Build());
        }

        var state = new PlaybackState.Builder()!
            .SetActions(
                PlaybackState.ActionPlay | PlaybackState.ActionPause | PlaybackState.ActionPlayPause |
                PlaybackState.ActionSkipToNext | PlaybackState.ActionSkipToPrevious | PlaybackState.ActionSeekTo |
                PlaybackState.ActionPlayFromMediaId | PlaybackState.ActionPlayFromSearch | PlaybackState.ActionSkipToQueueItem)!
            .SetState(
                _playback.IsPlaying ? PlaybackStateCode.Playing : PlaybackStateCode.Paused,
                (long)_playback.Position.TotalMilliseconds,
                _playback.IsPlaying ? 1f : 0f)!
            .SetActiveQueueItemId(_playback.CurrentIndex)!
            .Build();

        _session.SetPlaybackState(state);
    }

    /// <summary>Downloads the cover in the background and refreshes the notification when ready.</summary>
    private void LoadArtworkIfNeeded(string? url)
    {
        if (url == _artworkUrl)
            return;

        _artworkUrl = url;
        _artwork = null;
        if (url is null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                // Covers saved on the device are local paths; others are remote thumbnails.
                var bytes = File.Exists(url) ? await File.ReadAllBytesAsync(url) : await Http.GetByteArrayAsync(url);
                var bitmap = await BitmapFactory.DecodeByteArrayAsync(bytes, 0, bytes.Length);
                if (bitmap is null || url != _artworkUrl)
                    return;

                _artwork = bitmap;
                if (_playback.CurrentItem is not null)
                    _notifications.Notify(NotificationId, BuildNotification());
            }
            catch (Exception)
            {
                // Offline or broken thumbnail: the notification simply has no cover.
            }
        });
    }

    private Notification.Action CreateAction(int icon, string title, string action)
    {
        var intent = new Intent(this, typeof(MediaPlaybackService)).SetAction(action);
        var pendingIntent = PendingIntent.GetService(
            this, action.GetHashCode(), intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        return new Notification.Action.Builder(Icon.CreateWithResource(this, icon), title, pendingIntent).Build()!;
    }

    private PendingIntent? OpenAppIntent()
    {
        var launch = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        return launch is null
            ? null
            : PendingIntent.GetActivity(this, 0, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
    }

    private void CreateNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var channel = new NotificationChannel(ChannelId, L.T("Notif_PlaybackChannel"), NotificationImportance.Low)
        {
            Description = L.T("Notif_PlaybackChannelDescription"),
        };
        channel.SetShowBadge(false);
        _notifications.CreateNotificationChannel(channel);
    }

    /// <summary>The queue as Android Auto shows it; ids are queue positions.</summary>
    private void PublishQueue()
    {
        var queue = _playback.Queue;
        var current = Math.Max(0, _playback.CurrentIndex);
        var first = Math.Max(0, current - 10);
        var last = Math.Min(queue.Count, current + QueueWindow);

        // Republish only when the queue changed, not on every play/pause.
        var key = $"{queue.Count}:{first}:{last}:{(queue.Count > 0 ? queue[^1].Id : Guid.Empty)}";
        if (key == _publishedQueueKey)
            return;
        _publishedQueueKey = key;

        var items = new List<MediaSession.QueueItem>(last - first);
        for (var index = first; index < last; index++)
        {
            var entry = queue[index];
            items.Add(new MediaSession.QueueItem(Describe(entry.Id.ToString("N"), entry.Title, entry.Artist, entry.ThumbnailUrl), index));
        }

        _session.SetQueue(items);
    }

    private MediaBrowser.MediaItem ToMediaItem(BrowseNode node) =>
        new(Describe(node.Id, node.Title, node.Subtitle, node.Artwork),
            node.IsPlayable ? MediaItemFlags.Playable : MediaItemFlags.Browsable);

    private MediaDescription Describe(string id, string title, string? subtitle, string? artwork) =>
        new MediaDescription.Builder()
            .SetMediaId(id)!
            .SetTitle(title)!
            .SetSubtitle(subtitle)!
            .SetIconUri(ArtworkProvider.UriFor(this, artwork))!
            .Build()!;

    private void StopForegroundCompat()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(24))
            StopForeground(StopForegroundFlags.Remove);
        else
#pragma warning disable CA1422
            StopForeground(true);
#pragma warning restore CA1422
        IsForeground = false;
    }

    /// <summary>Media buttons: lock screen, Bluetooth devices, headsets, Android Auto...</summary>
    private sealed class SessionCallback(IPlaybackService playback, MediaBrowseTree browseTree) : MediaSession.Callback
    {
        /// <summary>A track or "Aleatorio" chosen in Android Auto's browser.</summary>
        public override void OnPlayFromMediaId(string? mediaId, Bundle? extras)
        {
            if (mediaId is null)
                return;

            _ = Task.Run(async () =>
            {
                if (await browseTree.ResolveAsync(mediaId) is not { } selection)
                    return;

                if (selection.Shuffle)
                    await playback.ShuffleAsync(selection.Items);
                else
                    await playback.PlayAsync(selection.Items, selection.StartIndex);
            });
        }

        /// <summary>Voice: "Pon … en Wired". An empty query means "play something".</summary>
        public override void OnPlayFromSearch(string? query, Bundle? extras) =>
            _ = Task.Run(async () =>
            {
                var items = await browseTree.SearchAsync(query);
                if (items.Count == 0)
                    return;

                if (string.IsNullOrWhiteSpace(query))
                    await playback.ShuffleAsync(items);
                else
                    await playback.PlayAsync(items);
            });

        public override void OnSkipToQueueItem(long id) => _ = playback.SkipToAsync((int)id);

        public override void OnPlay()
        {
            if (!playback.IsPlaying)
                playback.TogglePlayPause();
        }

        public override void OnPause() => playback.Pause();

        public override void OnSkipToNext() => _ = playback.NextAsync();

        public override void OnSkipToPrevious() => _ = playback.PreviousAsync();

        public override void OnSeekTo(long pos) => playback.Seek(TimeSpan.FromMilliseconds(pos));
    }
}
