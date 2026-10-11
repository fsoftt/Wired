namespace Wired.Presentation.Services;

/// <summary>
/// Permission to read the shared music folder. Needed to see files left by a previous installation.
/// </summary>
public interface IMusicFolderAccess
{
    /// <summary>Human readable location, e.g. "Música/Wired".</summary>
    string DisplayLocation { get; }

    /// <summary>Whether the music folder exists, e.g. left by a previous installation.</summary>
    bool FolderExists { get; }

    Task<bool> HasReadAccessAsync();

    /// <summary>Asks the user if needed.</summary>
    Task<bool> RequestReadAccessAsync();
}
