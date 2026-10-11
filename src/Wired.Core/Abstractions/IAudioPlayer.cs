using Wired.Core.Playback;

namespace Wired.Core.Abstractions;

/// <summary>
/// Low level audio output. Implemented per platform/library by the UI host.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    bool IsPlaying { get; }

    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    /// <summary>
    /// Loudness correction of the loaded track in dB (negative quiets, positive boosts),
    /// so every song plays at about the same volume.
    /// </summary>
    double GainDb { get; set; }

    /// <summary>Output level from 0 to 1 on top of <see cref="GainDb"/> (e.g. the sleep timer's fade out).</summary>
    float Volume { get; set; }

    /// <summary>Raised when the current file reaches its end.</summary>
    event EventHandler? PlaybackEnded;

    /// <summary>
    /// Raised when the platform pauses or resumes playback on its own
    /// (another app takes audio focus, headphones are unplugged...).
    /// </summary>
    event EventHandler? StateChangedExternally;

    /// <summary>Opens <paramref name="source"/>, taking ownership of its stream if it has one.</summary>
    Task LoadAsync(PlaybackSource source, CancellationToken cancellationToken = default);

    void Play();

    void Pause();

    void Stop();

    void Seek(TimeSpan position);
}
