namespace Wired.Core.Throttling;

public sealed record ThrottleOptions
{
    /// <summary>Minimum gap between background requests.</summary>
    public TimeSpan BackgroundInterval { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>Random extra delay added to each background request so traffic does not look automated.</summary>
    public TimeSpan BackgroundJitter { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Minimum gap between requests the user triggered (searches, streaming).</summary>
    public TimeSpan InteractiveInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>First pause after being rate-limited; doubles on every new limit.</summary>
    public TimeSpan InitialCooldown { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan MaxCooldown { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Background requests retry this many times after cooldowns before giving up.</summary>
    public int MaxBackgroundAttempts { get; init; } = 6;
}
