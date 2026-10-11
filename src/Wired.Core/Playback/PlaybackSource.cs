namespace Wired.Core.Playback;

/// <summary>What the audio player opens: a file on the device or a remote stream.</summary>
public abstract record PlaybackSource;

public sealed record FilePlaybackSource(string Path) : PlaybackSource;

/// <summary>A seekable stream of audio. Ownership passes to whoever loads it.</summary>
public sealed record StreamPlaybackSource(Stream Content) : PlaybackSource;
