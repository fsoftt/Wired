using Plugin.Maui.Audio;
using Wired.Core.Playback;
using IWiredAudioPlayer = Wired.Core.Abstractions.IAudioPlayer;

namespace Wired.App.Services;

/// <summary>
/// <see cref="IWiredAudioPlayer"/> backed by Plugin.Maui.Audio. Used on iOS, Mac Catalyst and Windows;
/// Android uses its own player to control wake locks and audio focus.
/// </summary>
public sealed class MauiAudioPlayer(IAudioManager audioManager) : IWiredAudioPlayer
{
    private double _gainDb;
    private float _volume = 1f;

    private Plugin.Maui.Audio.IAudioPlayer? _player;
    private Stream? _source;

    public event EventHandler? PlaybackEnded;

    // The plugin does not report interruptions; the OS handles them for us on these platforms.
    public event EventHandler? StateChangedExternally
    {
        add { }
        remove { }
    }

    public bool IsPlaying => _player?.IsPlaying ?? false;

    public TimeSpan Position => TimeSpan.FromSeconds(_player?.CurrentPosition ?? 0);

    public TimeSpan Duration => TimeSpan.FromSeconds(_player?.Duration ?? 0);

    /// <summary>Only cuts are applied here: the plugin cannot boost above full volume.</summary>
    public double GainDb
    {
        get => _gainDb;
        set
        {
            _gainDb = value;
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

    private void ApplyVolume()
    {
        if (_player is not null)
            _player.Volume = Math.Min(1, Core.Audio.LoudnessNormalization.ToAmplitude(Math.Min(0, _gainDb))) * _volume;
    }

    public async Task LoadAsync(PlaybackSource source, CancellationToken cancellationToken = default)
    {
        Release();

        _source = await OpenAsync(source, cancellationToken);
        _player = audioManager.CreatePlayer(_source);
        _player.PlaybackEnded += OnPlaybackEnded;
        ApplyVolume();
    }

    /// <summary>
    /// Plugin.Maui.Audio reads the whole stream up front, so remote audio is buffered in memory
    /// (never written to disk).
    /// </summary>
    private static async Task<Stream> OpenAsync(PlaybackSource source, CancellationToken cancellationToken)
    {
        if (source is FilePlaybackSource file)
            return File.OpenRead(file.Path);

        var buffer = new MemoryStream();
        await using (var remote = ((StreamPlaybackSource)source).Content)
        {
            await remote.CopyToAsync(buffer, cancellationToken);
        }

        buffer.Position = 0;
        return buffer;
    }

    public void Play() => _player?.Play();

    public void Pause() => _player?.Pause();

    public void Stop() => Release();

    public void Seek(TimeSpan position)
    {
        if (_player is { CanSeek: true })
            _player.Seek(position.TotalSeconds);
    }

    public void Dispose() => Release();

    private void OnPlaybackEnded(object? sender, EventArgs e) => PlaybackEnded?.Invoke(this, EventArgs.Empty);

    private void Release()
    {
        if (_player is not null)
        {
            _player.PlaybackEnded -= OnPlaybackEnded;
            _player.Stop();
            _player.Dispose();
            _player = null;
        }

        _source?.Dispose();
        _source = null;
    }
}
