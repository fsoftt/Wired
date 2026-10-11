namespace Wired.Core.Import;

/// <param name="SourceId">Music source plugin used to find and download the tracks.</param>
/// <param name="CreatePlaylist">Create a local playlist with the imported tracks, in the file's order.</param>
/// <param name="AddToLikes">Also mark the imported tracks as "Me gusta".</param>
public sealed record ImportOptions(string SourceId, bool CreatePlaylist, bool AddToLikes);
