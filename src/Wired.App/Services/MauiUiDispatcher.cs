using Wired.Presentation.Services;

namespace Wired.App.Services;

public sealed class MauiUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => MainThread.BeginInvokeOnMainThread(action);
}
