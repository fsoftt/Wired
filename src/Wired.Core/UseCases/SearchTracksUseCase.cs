using Wired.Core.Models;
using Wired.Core.Plugins;

namespace Wired.Core.UseCases;

public sealed class SearchTracksUseCase(IPluginCatalog plugins)
{
    public const int DefaultMaxResults = 25;

    public async Task<IReadOnlyList<RemoteTrack>> ExecuteAsync(
        string pluginId,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var plugin = plugins.Get(pluginId);

        // Plugins do network I/O: never run them on the caller's (UI) thread. Android forbids it.
        return await Task.Run(() => plugin.SearchAsync(query.Trim(), DefaultMaxResults, cancellationToken), cancellationToken);
    }
}
