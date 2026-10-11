namespace Wired.Presentation.Services;

/// <summary>An entry of a contextual menu (long press on a track).</summary>
public sealed record MenuOption(string Label, Func<Task> Run);
