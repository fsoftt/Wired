using CommunityToolkit.Mvvm.Input;
using Wired.Core.Playback;

namespace Wired.Presentation.ViewModels;

/// <summary>An upcoming entry of the play queue.</summary>
public sealed partial class QueueEntryViewModel(PlaybackItem item, int queueIndex, QueueViewModel owner)
{
    public PlaybackItem Item { get; } = item;

    /// <summary>Position in the playback queue when this entry was created.</summary>
    public int QueueIndex { get; } = queueIndex;

    public string Title => Item.Title;

    public string Artist => Item.Artist;

    public string? ThumbnailUrl => Item.ThumbnailUrl;

    public bool IsStream => Item.IsStream;

    [RelayCommand]
    private Task PlayAsync() => owner.SkipToAsync(this);

    [RelayCommand]
    private Task RemoveAsync() => owner.RemoveAsync(this);
}
