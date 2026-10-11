using System.Globalization;
using System.Text.RegularExpressions;

namespace Wired.Core.Lyrics;

/// <summary>
/// Parses the LRC format: "[mm:ss.xx]text". Supports several time tags per line and the [offset:ms] tag;
/// other metadata tags ([ar:], [ti:]...) are ignored.
/// </summary>
public static partial class LrcParser
{
    public static IReadOnlyList<LyricLine> Parse(string lrc)
    {
        var offset = TimeSpan.Zero;
        var lines = new List<LyricLine>();

        foreach (var rawLine in lrc.Split('\n'))
        {
            var line = rawLine.Trim();

            var offsetMatch = OffsetTag().Match(line);
            if (offsetMatch.Success)
            {
                // Positive offsets make lyrics appear earlier.
                offset = TimeSpan.FromMilliseconds(int.Parse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture));
                continue;
            }

            var times = new List<TimeSpan>();
            var index = 0;
            Match match;
            while ((match = TimeTag().Match(line, index)).Success && match.Index == index)
            {
                times.Add(ParseTime(match));
                index += match.Length;
            }

            if (times.Count == 0)
                continue;

            var text = line[index..].Trim();
            lines.AddRange(times.Select(time => new LyricLine(time, text)));
        }

        return lines
            .Select(l => l with { Time = l.Time - offset < TimeSpan.Zero ? TimeSpan.Zero : l.Time - offset })
            .OrderBy(l => l.Time)
            .ToList();
    }

    private static TimeSpan ParseTime(Match match)
    {
        var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

        // ".5" = 500 ms, ".50" = 500 ms (centiseconds), ".500" = 500 ms.
        var fraction = match.Groups[3].Value;
        var milliseconds = fraction.Length == 0
            ? 0
            : int.Parse(fraction, CultureInfo.InvariantCulture) * (int)Math.Pow(10, 3 - fraction.Length);

        return new TimeSpan(0, 0, minutes, seconds, milliseconds);
    }

    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\[offset:\s*([+-]?\d+)\]$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();
}
