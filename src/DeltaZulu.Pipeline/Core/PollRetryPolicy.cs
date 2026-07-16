namespace DeltaZulu.Pipeline.Core;

/// <summary>
/// Bounded exponential-backoff policy for polling inputs that must tolerate transient
/// read failures without terminating the stream on the first error. The policy is a pure,
/// host-neutral value so it can be unit-tested independently of any platform read loop.
/// </summary>
/// <remarks>
/// A polling loop keeps a running count of <em>consecutive</em> failures, resetting it to
/// zero after each successful read. On each failure it asks the policy how long to wait
/// (<see cref="GetBackoffDelay"/>) and whether the failure count has crossed the escalation
/// threshold (<see cref="ShouldEscalate"/>), at which point the loop should surface a terminal
/// error rather than continue retrying a channel that is genuinely broken (deleted, access
/// revoked) instead of momentarily unavailable.
/// </remarks>
public sealed record PollRetryPolicy
{
    /// <summary>Delay applied after the first failure; doubled for each subsequent consecutive failure up to <see cref="MaxDelay"/>.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound on the backoff delay regardless of how many consecutive failures have occurred.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Number of consecutive failures tolerated before the loop should escalate to a terminal error.</summary>
    public int MaxConsecutiveFailures { get; init; } = 5;

    /// <summary>
    /// Returns the backoff delay to wait before the next retry, given the number of consecutive
    /// failures observed so far (1 for the first failure). The delay grows exponentially from
    /// <see cref="BaseDelay"/> and is capped at <see cref="MaxDelay"/>.
    /// </summary>
    public TimeSpan GetBackoffDelay(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.Zero;
        }

        // Compute BaseDelay * 2^(n-1) in ticks with overflow-safe saturation to MaxDelay.
        var maxTicks = MaxDelay.Ticks;
        var baseTicks = BaseDelay.Ticks;
        if (baseTicks <= 0)
        {
            return TimeSpan.Zero;
        }

        var shift = consecutiveFailures - 1;
        // Shifts of 63+ (or a multiplier that would exceed MaxDelay) saturate to MaxDelay.
        if (shift >= 63)
        {
            return MaxDelay;
        }

        var multiplier = 1L << shift;
        if (baseTicks > maxTicks / multiplier)
        {
            return MaxDelay;
        }

        var scaledTicks = baseTicks * multiplier;
        return scaledTicks >= maxTicks ? MaxDelay : TimeSpan.FromTicks(scaledTicks);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the number of consecutive failures has reached
    /// <see cref="MaxConsecutiveFailures"/>, indicating the caller should stop retrying and
    /// surface a terminal error.
    /// </summary>
    public bool ShouldEscalate(int consecutiveFailures) => consecutiveFailures >= MaxConsecutiveFailures;
}
