namespace Wired.Core.Throttling;

/// <summary>
/// Thrown by plugins when their service asks to slow down (HTTP 429, bot checks...).
/// </summary>
public sealed class SourceRateLimitedException(string message, TimeSpan? retryAfter = null, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>How long the service asked to wait, when it said so.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
