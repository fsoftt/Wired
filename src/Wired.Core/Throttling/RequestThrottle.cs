namespace Wired.Core.Throttling;

/// <summary>
/// Paces the requests sent to one music source. Background requests are spaced and wait out
/// cooldowns; interactive ones only keep a small gap, so the user never waits behind an import.
/// </summary>
public sealed class RequestThrottle(string sourceId, ThrottleOptions options, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _backgroundTurn = new(1, 1);
    private readonly Lock _lock = new();

    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private DateTimeOffset _lastInteractive = DateTimeOffset.MinValue;
    private TimeSpan _nextCooldown = options.InitialCooldown;

    public string SourceId { get; } = sourceId;

    public ThrottleOptions Options { get; } = options;

    /// <summary>Until when background requests are paused, if they are.</summary>
    public DateTimeOffset? CooldownUntil { get; private set; }

    public bool IsCoolingDown => CooldownUntil is { } until && until > _time.GetUtcNow();

    /// <summary>Raised when a cooldown starts or ends. May be raised from any thread.</summary>
    public event EventHandler? StateChanged;

    public Task WaitTurnAsync(CancellationToken cancellationToken = default) =>
        RequestPriority.IsBackground ? WaitBackgroundAsync(cancellationToken) : WaitInteractiveAsync(cancellationToken);

    /// <summary>The service asked us to slow down: pause background work with exponential backoff.</summary>
    public void ReportRateLimited(TimeSpan? retryAfter)
    {
        lock (_lock)
        {
            var pause = retryAfter ?? _nextCooldown;
            CooldownUntil = _time.GetUtcNow() + pause;
            _nextCooldown = TimeSpan.FromTicks(Math.Min(_nextCooldown.Ticks * 2, Options.MaxCooldown.Ticks));
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A request went through: the next limit starts again from the shortest pause.</summary>
    public void ReportSuccess()
    {
        var ended = false;
        lock (_lock)
        {
            _nextCooldown = Options.InitialCooldown;
            if (CooldownUntil is not null && !IsCoolingDown)
            {
                CooldownUntil = null;
                ended = true;
            }
        }

        if (ended)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task WaitBackgroundAsync(CancellationToken cancellationToken)
    {
        // One background request at a time takes its turn; interactive requests never wait on this.
        await _backgroundTurn.WaitAsync(cancellationToken);
        try
        {
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * Options.BackgroundJitter.TotalMilliseconds);
            while (true)
            {
                TimeSpan wait;
                lock (_lock)
                {
                    var notBefore = _lastRequest + Options.BackgroundInterval + jitter;
                    if (CooldownUntil is { } until && until > notBefore)
                        notBefore = until;

                    var now = _time.GetUtcNow();
                    wait = notBefore - now;
                    if (wait <= TimeSpan.Zero)
                    {
                        _lastRequest = now;
                        return;
                    }
                }

                // Re-checked after waking: a new cooldown may have started meanwhile.
                await Task.Delay(wait, _time, cancellationToken);
            }
        }
        finally
        {
            _backgroundTurn.Release();
        }
    }

    private async Task WaitInteractiveAsync(CancellationToken cancellationToken)
    {
        TimeSpan wait;
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            var slot = _lastInteractive + Options.InteractiveInterval;
            if (slot < now)
                slot = now;

            _lastInteractive = slot;
            _lastRequest = slot;
            wait = slot - now;
        }

        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, _time, cancellationToken);
    }
}
