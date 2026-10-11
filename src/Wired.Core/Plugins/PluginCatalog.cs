namespace Wired.Core.Plugins;

public sealed class PluginCatalog : IPluginCatalog
{
    private readonly Dictionary<string, IMusicSourcePlugin> _pluginsById;

    public PluginCatalog(IEnumerable<IMusicSourcePlugin> plugins)
    {
        Plugins = plugins.ToList();

        var duplicated = Plugins.GroupBy(p => p.Info.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
            throw new InvalidOperationException($"Plugin id '{duplicated.Key}' is registered more than once.");

        _pluginsById = Plugins.ToDictionary(p => p.Info.Id);
    }

    public IReadOnlyList<IMusicSourcePlugin> Plugins { get; }

    public IMusicSourcePlugin Get(string pluginId) =>
        _pluginsById.TryGetValue(pluginId, out var plugin)
            ? plugin
            : throw new PluginNotFoundException(pluginId);
}
