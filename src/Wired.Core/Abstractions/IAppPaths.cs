namespace Wired.Core.Abstractions;

/// <summary>
/// Platform specific locations, provided by the UI host.
/// </summary>
public interface IAppPaths
{
    /// <summary>
    /// Where downloaded audio files are stored. Ideally a shared folder (e.g. Music/Wired)
    /// that survives uninstalling the app.
    /// </summary>
    string MusicDirectory { get; }

    /// <summary>Scratch space for in-progress downloads.</summary>
    string CacheDirectory { get; }

    /// <summary>Where the app keeps its own data (library, playlists, likes...).</summary>
    string DataDirectory { get; }
}
