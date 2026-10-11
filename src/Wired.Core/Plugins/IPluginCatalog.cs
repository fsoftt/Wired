namespace Wired.Core.Plugins;

public interface IPluginCatalog
{
    IReadOnlyList<IMusicSourcePlugin> Plugins { get; }

    /// <exception cref="PluginNotFoundException">No plugin is registered with <paramref name="pluginId"/>.</exception>
    IMusicSourcePlugin Get(string pluginId);
}
