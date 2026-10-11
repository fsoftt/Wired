namespace Wired.Core.Plugins;

/// <summary>
/// Metadata used by the UI to present a music source plugin.
/// </summary>
/// <param name="Id">Stable, unique identifier (e.g. "mysource").</param>
/// <param name="DisplayName">Human readable name.</param>
/// <param name="Description">Short description shown to the user.</param>
/// <param name="AccentColor">Brand color in hex format (#RRGGBB).</param>
public sealed record PluginInfo(
    string Id,
    string DisplayName,
    string Description,
    string AccentColor);
