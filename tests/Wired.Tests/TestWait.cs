namespace Wired.Tests;

internal static class TestWait
{
    public static async Task UntilAsync(Func<bool> condition, int timeoutMs = 15000)
    {
        using var timeout = new CancellationTokenSource(timeoutMs);
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }
}
