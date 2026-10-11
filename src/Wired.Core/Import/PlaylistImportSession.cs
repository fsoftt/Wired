using System.Text.Json.Serialization;
using Wired.Core.Models;

namespace Wired.Core.Import;

public enum ImportItemStatus
{
    Pending,
    Downloading,
    Done,
    NotFound,
    Failed,
    Canceled,
}

/// <summary>One track of an import and what happened to it.</summary>
public sealed record ImportItem(
    int Index,
    ImportedTrack Track,
    ImportItemStatus Status = ImportItemStatus.Pending,
    RemoteTrack? Match = null,
    Guid? LocalTrackId = null,
    string? Error = null,
    bool AlreadyInLibrary = false)
{
    [JsonIgnore]
    public bool IsFinished => Status is not (ImportItemStatus.Pending or ImportItemStatus.Downloading);
}

/// <summary>
/// A playlist import in progress or finished. Persisted after every change so it resumes
/// when the app is reopened.
/// </summary>
public sealed record PlaylistImportSession(
    Guid Id,
    string Name,
    string SourceId,
    Guid? PlaylistId,
    bool AddToLikes,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ImportItem> Items)
{
    [JsonIgnore]
    public int Total => Items.Count;

    [JsonIgnore]
    public int Done => Items.Count(i => i.Status == ImportItemStatus.Done);

    /// <summary>Tracks found in the library, so neither searched nor downloaded.</summary>
    [JsonIgnore]
    public int AlreadyInLibrary => Items.Count(i => i.AlreadyInLibrary);

    [JsonIgnore]
    public int NotFound => Items.Count(i => i.Status == ImportItemStatus.NotFound);

    [JsonIgnore]
    public int Failed => Items.Count(i => i.Status is ImportItemStatus.Failed);

    [JsonIgnore]
    public int Finished => Items.Count(i => i.IsFinished);

    [JsonIgnore]
    public bool IsRunning => Items.Any(i => !i.IsFinished);
}
