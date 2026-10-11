namespace Wired.Presentation.Services;

/// <summary>
/// Marshals work to the UI thread. Keeps view models free of any UI framework reference.
/// </summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
