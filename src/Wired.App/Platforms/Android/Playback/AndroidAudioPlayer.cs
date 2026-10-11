using Android.Content;
using Android.Media;
using Android.Media.Audiofx;
using Android.OS;
using Wired.Core.Abstractions;
using Wired.Core.Audio;
using Wired.Core.Playback;
using Context = Android.Content.Context;

namespace Wired.App.Playback;

/// <summary>
/// Native Android player: keeps the CPU awake while playing (wake lock), cooperates with other apps
/// through audio focus and pauses when headphones are unplugged.
/// </summary>
public sealed class AndroidAudioPlayer : IAudioPlayer
{
    private const float DuckVolume = 0.2f;

    private readonly Context _context = Platform.AppContext;
    private readonly AudioManager _audioManager;
    private readonly AudioAttributes _attributes;
    private readonly FocusListener _focusListener;
    private readonly NoisyAudioReceiver _noisyReceiver;

    private MediaPlayer? _player;
    private LoudnessEnhancer? _enhancer;
    private double _gainDb;
    private float _volume = 1f;
    private bool _ducked;
    private string? _temporaryFile;
    private AudioFocusRequestClass? _focusRequest;
    private bool _hasFocus;
    private bool _resumeOnFocusGain;
    private bool _noisyReceiverRegistered;

    public AndroidAudioPlayer()
    {
        _audioManager = (AudioManager)_context.GetSystemService(Context.AudioService)!;
        _attributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Media)!
            .SetContentType(AudioContentType.Music)!
            .Build()!;
        _focusListener = new FocusListener(OnAudioFocusChanged);
        _noisyReceiver = new NoisyAudioReceiver(PauseBySystem);
    }

    public event EventHandler? PlaybackEnded;

    public event EventHandler? StateChangedExternally;

    public bool IsPlaying => _player?.IsPlaying ?? false;

    public TimeSpan Position => TimeSpan.FromMilliseconds(_player?.CurrentPosition ?? 0);

    public TimeSpan Duration => TimeSpan.FromMilliseconds(Math.Max(0, _player?.Duration ?? 0));

    public double GainDb
    {
        get => _gainDb;
        set
        {
            _gainDb = value;
            ApplyBoost();
            ApplyVolume();
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = value;
            ApplyVolume();
        }
    }

    public async Task LoadAsync(PlaybackSource source, CancellationToken cancellationToken = default)
    {
        ReleasePlayer();

        var player = source switch
        {
            FilePlaybackSource file => CreatePlayer(p => p.SetDataSource(file.Path)),
            StreamPlaybackSource stream => await CreateStreamPlayerAsync(stream.Content, cancellationToken),
            _ => throw new NotSupportedException($"Unknown playback source {source.GetType().Name}."),
        };
        player.Completion += OnCompletion;
        _player = player;

        // Asynchronous preparation keeps the UI thread free.
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPrepared(object? sender, EventArgs e) => prepared.TrySetResult();
        void OnError(object? sender, MediaPlayer.ErrorEventArgs e)
        {
            e.Handled = true;
            prepared.TrySetException(new IOException($"No se pudo abrir el audio ({e.What}, {e.Extra})."));
        }

        player.Prepared += OnPrepared;
        player.Error += OnError;
        try
        {
            player.PrepareAsync();
            await prepared.Task.WaitAsync(cancellationToken);
        }
        catch
        {
            if (_player == player)
                ReleasePlayer();
            throw;
        }
        finally
        {
            player.Prepared -= OnPrepared;
            player.Error -= OnError;
        }
    }

    /// <summary>
    /// Buffers the remote audio in memory first: MediaPlayer sniffs a custom data source synchronously
    /// (from the media server), so reads must never touch the network. If the device still rejects a
    /// <see cref="MediaDataSource"/>, falls back to a temporary cache file deleted on release.
    /// </summary>
    private async Task<MediaPlayer> CreateStreamPlayerAsync(System.IO.Stream remote, CancellationToken cancellationToken)
    {
        // Reading and disposing the remote stream both touch the network: keep them off the main thread.
        var audio = await Task.Run(
            async () =>
            {
                await using (remote)
                {
                    using var buffer = new MemoryStream();
                    await remote.CopyToAsync(buffer, cancellationToken);
                    return buffer.ToArray();
                }
            },
            cancellationToken);

        try
        {
            // The data source gets its own read-only view: MediaPlayer closes it when it gives up.
            return CreatePlayer(p => p.SetDataSource(new StreamMediaDataSource(new MemoryStream(audio, writable: false))));
        }
        catch (Java.Lang.RuntimeException)
        {
            var path = Path.Combine(FileSystem.CacheDirectory, $"stream-{Guid.NewGuid():N}.audio");
            await File.WriteAllBytesAsync(path, audio, cancellationToken);
            _temporaryFile = path;

            return CreatePlayer(p => p.SetDataSource(path));
        }
    }

    private MediaPlayer CreatePlayer(Action<MediaPlayer> setDataSource)
    {
        var player = new MediaPlayer();
        try
        {
            player.SetAudioAttributes(_attributes);
            player.SetWakeMode(_context, WakeLockFlags.Partial);
            setDataSource(player);
            return player;
        }
        catch
        {
            player.Release();
            player.Dispose();
            throw;
        }
    }

    public void Play()
    {
        if (_player is null || !RequestAudioFocus())
            return;

        _ducked = false;
        ApplyVolume();
        _player.Start();
        RegisterNoisyReceiver();
    }

    public void Pause()
    {
        _resumeOnFocusGain = false;
        _player?.Pause();
        UnregisterNoisyReceiver();
    }

    public void Stop()
    {
        ReleasePlayer();
        AbandonAudioFocus();
    }

    public void Seek(TimeSpan position) => _player?.SeekTo((int)position.TotalMilliseconds);

    public void Dispose() => Stop();

    private void OnCompletion(object? sender, EventArgs e)
    {
        UnregisterNoisyReceiver();
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnAudioFocusChanged(AudioFocus change)
    {
        switch (change)
        {
            case AudioFocus.Gain:
                _hasFocus = true;
                _ducked = false;
                ApplyVolume();
                if (_resumeOnFocusGain && _player is { IsPlaying: false })
                {
                    _resumeOnFocusGain = false;
                    _player.Start();
                    RegisterNoisyReceiver();
                    StateChangedExternally?.Invoke(this, EventArgs.Empty);
                }
                break;

            case AudioFocus.LossTransientCanDuck:
                _ducked = true;
                ApplyVolume();
                break;

            case AudioFocus.LossTransient:
                // e.g. a phone call: pause and resume when it ends.
                if (IsPlaying)
                {
                    PauseBySystem();
                    _resumeOnFocusGain = true;
                }
                break;

            case AudioFocus.Loss:
                // Another app started playing music: stop for good.
                _hasFocus = false;
                PauseBySystem();
                break;
        }
    }

    /// <summary>Quieter songs are cut with the player volume (it cannot go above full).</summary>
    private void ApplyVolume()
    {
        var cut = (float)LoudnessNormalization.ToAmplitude(Math.Min(0, _gainDb));
        var level = Math.Clamp(cut * _volume * (_ducked ? DuckVolume : 1f), 0f, 1f);
        _player?.SetVolume(level, level);
    }

    /// <summary>Quiet songs are boosted with the system's loudness enhancer, which avoids clipping.</summary>
    private void ApplyBoost()
    {
        if (_player is null)
            return;

        var boostMillibels = (int)Math.Round(Math.Max(0, _gainDb) * 100);
        try
        {
            if (boostMillibels == 0)
            {
                _enhancer?.SetEnabled(false);
                return;
            }

            _enhancer ??= new LoudnessEnhancer(_player.AudioSessionId);
            _enhancer.SetTargetGain(boostMillibels);
            _enhancer.SetEnabled(true);
        }
        catch (Java.Lang.Exception)
        {
            // Some devices lack the effect: the song plays without the boost.
            ReleaseEnhancer();
        }
    }

    private void ReleaseEnhancer()
    {
        if (_enhancer is null)
            return;

        _enhancer.Release();
        _enhancer.Dispose();
        _enhancer = null;
    }

    private void PauseBySystem()
    {
        if (!IsPlaying)
            return;

        Pause();
        StateChangedExternally?.Invoke(this, EventArgs.Empty);
    }

    private bool RequestAudioFocus()
    {
        if (_hasFocus)
            return true;

        AudioFocusRequest result;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            _focusRequest ??= new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                .SetAudioAttributes(_attributes)
                .SetOnAudioFocusChangeListener(_focusListener)
                .Build()!;
            result = _audioManager.RequestAudioFocus(_focusRequest);
        }
        else
        {
#pragma warning disable CA1422 // Legacy API for Android < 8.0
            result = _audioManager.RequestAudioFocus(_focusListener, global::Android.Media.Stream.Music, AudioFocus.Gain);
#pragma warning restore CA1422
        }

        _hasFocus = result == AudioFocusRequest.Granted;
        return _hasFocus;
    }

    private void AbandonAudioFocus()
    {
        if (!_hasFocus)
            return;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            if (_focusRequest is not null)
                _audioManager.AbandonAudioFocusRequest(_focusRequest);
        }
        else
        {
#pragma warning disable CA1422
            _audioManager.AbandonAudioFocus(_focusListener);
#pragma warning restore CA1422
        }

        _hasFocus = false;
    }

    private void RegisterNoisyReceiver()
    {
        if (_noisyReceiverRegistered)
            return;

        var filter = new IntentFilter(AudioManager.ActionAudioBecomingNoisy);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            _context.RegisterReceiver(_noisyReceiver, filter, ReceiverFlags.NotExported);
        else
            _context.RegisterReceiver(_noisyReceiver, filter);

        _noisyReceiverRegistered = true;
    }

    private void UnregisterNoisyReceiver()
    {
        if (!_noisyReceiverRegistered)
            return;

        _context.UnregisterReceiver(_noisyReceiver);
        _noisyReceiverRegistered = false;
    }

    private void ReleasePlayer()
    {
        UnregisterNoisyReceiver();
        ReleaseEnhancer();

        if (_player is not null)
        {
            _player.Completion -= OnCompletion;
            _player.Release();
            _player.Dispose();
            _player = null;
        }

        DeleteTemporaryFile();
    }

    private void DeleteTemporaryFile()
    {
        if (_temporaryFile is null)
            return;

        try
        {
            File.Delete(_temporaryFile);
        }
        catch (IOException)
        {
            // The OS cleans the cache directory eventually.
        }

        _temporaryFile = null;
    }

    private sealed class FocusListener(Action<AudioFocus> onChange)
        : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        public void OnAudioFocusChange(AudioFocus focusChange) => onChange(focusChange);
    }

    /// <summary>Headphones unplugged or Bluetooth disconnected.</summary>
    private sealed class NoisyAudioReceiver(Action onNoisy) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == AudioManager.ActionAudioBecomingNoisy)
                onNoisy();
        }
    }
}
