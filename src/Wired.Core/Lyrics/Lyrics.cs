using System.Text.Json.Serialization;

namespace Wired.Core.Lyrics;

/// <summary>
/// Lyrics of a track: synchronized (LRC) when available, plain text otherwise.
/// </summary>
public sealed record Lyrics(string? SyncedLrc, string? PlainText, bool IsInstrumental = false)
{
    private IReadOnlyList<LyricLine>? _lines;

    /// <summary>Parsed <see cref="SyncedLrc"/>, ordered by time. Empty when not synchronized.</summary>
    [JsonIgnore]
    public IReadOnlyList<LyricLine> Lines => _lines ??= SyncedLrc is null ? [] : LrcParser.Parse(SyncedLrc);

    [JsonIgnore]
    public bool IsSynced => Lines.Count > 0;

    /// <summary>Index of the line being sung at <paramref name="position"/>, or -1 before the first line.</summary>
    public int IndexAt(TimeSpan position)
    {
        var lines = Lines;
        int low = 0, high = lines.Count - 1, found = -1;

        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (lines[middle].Time <= position)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }
}
