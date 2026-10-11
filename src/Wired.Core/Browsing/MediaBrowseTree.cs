using Wired.Core.Abstractions;
using Wired.Core.Collections;
using Wired.Core.Import;
using Wired.Core.Localization;
using Wired.Core.Models;
using Wired.Core.Playback;

namespace Wired.Core.Browsing;

/// <summary>
/// The library as a tree for external browsers such as Android Auto: Biblioteca, Me gusta and
/// Listas, each starting with a shuffle entry. Media ids encode the context so playing a track
/// plays its list from that track.
/// </summary>
/// <remarks>
/// Ids: "root", "library", "likes", "playlists", "playlist|{id}"; playable: "track|{context}|{trackId}"
/// and "shuffle|{context}", where context is "library", "likes" or "playlist:{id}".
/// </remarks>
public sealed class MediaBrowseTree(ITrackLibrary library, PlaylistService playlists, LikesService likes)
{
    public const string RootId = "root";
    private const string LibraryId = "library";
    private const string LikesId = "likes";
    private const string PlaylistsId = "playlists";
    private const string PlaylistPrefix = "playlist|";
    private const string TrackPrefix = "track|";
    private const string ShufflePrefix = "shuffle|";
    private const string PlaylistContextPrefix = "playlist:";

    public async Task<IReadOnlyList<BrowseNode>> GetChildrenAsync(string parentId, CancellationToken cancellationToken = default)
    {
        await likes.InitializeAsync(cancellationToken);

        switch (parentId)
        {
            case RootId:
                var all = await library.GetAllAsync(cancellationToken);
                var lists = await playlists.GetAllAsync(cancellationToken);
                return
                [
                    new BrowseNode(LibraryId, L.T("Tab_Library"), Count(all.Count), null, IsPlayable: false),
                    new BrowseNode(LikesId, L.T("Likes_Title"), Count(likes.Count), null, IsPlayable: false),
                    new BrowseNode(PlaylistsId, L.T("Tab_Playlists"), L.N("Playlists", lists.Count), null, IsPlayable: false),
                ];

            case PlaylistsId:
                return (await playlists.GetAllAsync(cancellationToken))
                    .Select(p => new BrowseNode(PlaylistPrefix + p.Id.ToString("N"), p.Name, Count(p.TrackIds.Count), null, IsPlayable: false))
                    .ToList();

            default:
                var context = ContextOfFolder(parentId);
                return context is null ? [] : TracksWithShuffle(context, await TracksOfAsync(context, cancellationToken));
        }
    }

    /// <summary>What to play for a playable id: the whole context list and where to start.</summary>
    public async Task<(IReadOnlyList<PlaybackItem> Items, int StartIndex, bool Shuffle)?> ResolveAsync(string mediaId, CancellationToken cancellationToken = default)
    {
        if (mediaId.StartsWith(ShufflePrefix, StringComparison.Ordinal))
        {
            var tracks = await TracksOfAsync(mediaId[ShufflePrefix.Length..], cancellationToken);
            return tracks.Count == 0 ? null : (ToItems(tracks), 0, true);
        }

        if (!mediaId.StartsWith(TrackPrefix, StringComparison.Ordinal))
            return null;

        var parts = mediaId[TrackPrefix.Length..].Split('|');
        if (parts.Length != 2 || !Guid.TryParse(parts[1], out var trackId))
            return null;

        var list = await TracksOfAsync(parts[0], cancellationToken);
        var index = list.ToList().FindIndex(t => t.Id == trackId);
        return index < 0 ? null : (ToItems(list), index, false);
    }

    /// <summary>Voice search ("pon … en Wired"): matching tracks by title or artist; everything when the query is empty.</summary>
    public async Task<IReadOnlyList<PlaybackItem>> SearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        var all = await library.GetAllAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(query))
            return ToItems(all);

        var words = Import.TrackMatcher.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return ToItems(all
            .Where(t =>
            {
                var text = Import.TrackMatcher.Normalize($"{t.Title} {t.Artist} {t.Album}");
                return words.All(w => text.Contains(w, StringComparison.Ordinal));
            })
            .ToList());
    }

    private static string? ContextOfFolder(string folderId) => folderId switch
    {
        LibraryId => LibraryId,
        LikesId => LikesId,
        _ when folderId.StartsWith(PlaylistPrefix, StringComparison.Ordinal) => PlaylistContextPrefix + folderId[PlaylistPrefix.Length..],
        _ => null,
    };

    private async Task<IReadOnlyList<LocalTrack>> TracksOfAsync(string context, CancellationToken cancellationToken)
    {
        if (context == LibraryId)
            return await library.GetAllAsync(cancellationToken);

        if (context == LikesId)
            return await likes.GetLikedTracksAsync(cancellationToken);

        if (context.StartsWith(PlaylistContextPrefix, StringComparison.Ordinal)
            && Guid.TryParse(context[PlaylistContextPrefix.Length..], out var playlistId)
            && await playlists.FindAsync(playlistId, cancellationToken) is { } playlist)
        {
            return await playlists.GetTracksAsync(playlist, cancellationToken);
        }

        return [];
    }

    private static List<BrowseNode> TracksWithShuffle(string context, IReadOnlyList<LocalTrack> tracks)
    {
        var nodes = new List<BrowseNode>(tracks.Count + 1);
        if (tracks.Count > 1)
            nodes.Add(new BrowseNode(ShufflePrefix + context, L.T("Common_Shuffle"), Count(tracks.Count), null, IsPlayable: true));

        nodes.AddRange(tracks.Select(t => new BrowseNode($"{TrackPrefix}{context}|{t.Id:N}", t.Title, t.Artist, t.Artwork, IsPlayable: true)));
        return nodes;
    }

    private static List<PlaybackItem> ToItems(IEnumerable<LocalTrack> tracks) => tracks.Select(PlaybackItem.FromLocal).ToList();

    private static string Count(int count) => L.N("Songs", count);
}
