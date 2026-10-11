using System.Text.RegularExpressions;
using Wired.Core.Downloads;
using Wired.Core.Models;
using Wired.Core.Plugins;

namespace Wired.Core.UseCases;

/// <summary>
/// Downloads what the user shared to Wired from another app: finds the first link in the shared
/// text and asks each music source whether it is one of theirs.
/// </summary>
public sealed partial class DownloadSharedLinkUseCase(IPluginCatalog plugins, IDownloadQueue downloads)
{
    /// <returns>The queued track, or null when no source recognizes the shared content.</returns>
    public async Task<RemoteTrack?> ExecuteAsync(string sharedText, CancellationToken cancellationToken = default)
    {
        foreach (var link in FindLinks(sharedText))
        {
            foreach (var plugin in plugins.Plugins)
            {
                // Plugins do network I/O: never on the caller's (UI) thread.
                var track = await Task.Run(() => plugin.ResolveLinkAsync(link, cancellationToken), cancellationToken);
                if (track is not null)
                {
                    downloads.Enqueue(track);
                    return track;
                }
            }
        }

        return null;
    }

    /// <summary>Apps share links inside text ("Mira esto: https://example.com/song/…").</summary>
    public static IEnumerable<Uri> FindLinks(string text) =>
        UrlPattern().Matches(text)
            .Select(m => Uri.TryCreate(m.Value.TrimEnd('.', ',', ')', '!', '?'), UriKind.Absolute, out var uri) ? uri : null)
            .OfType<Uri>();

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
