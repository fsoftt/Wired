namespace Wired.Presentation.Services;

/// <summary>Whether the OS may throttle the app in background (e.g. Android battery optimizations).</summary>
public interface IBackgroundExecution
{
    bool IsRestricted { get; }

    /// <summary>Opens the system dialog that lets the user lift the restriction.</summary>
    Task RequestUnrestrictedAsync();
}
