namespace Wired.Core.Plugins;

public sealed class PluginNotFoundException(string pluginId)
    : Exception($"No music source plugin registered with id '{pluginId}'.")
{
    public string PluginId { get; } = pluginId;
}
