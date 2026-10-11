namespace Wired.Core.Resilience;

public sealed record ResilienceOptions
{
    /// <summary>Background calls are retried this many times after network failures.</summary>
    public int MaxBackgroundAttempts { get; init; } = 8;

    /// <summary>Wait after the first failure once online again; doubles on every retry.</summary>
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(2);
}
