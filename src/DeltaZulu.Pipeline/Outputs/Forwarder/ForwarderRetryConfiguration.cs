namespace DeltaZulu.Pipeline.Outputs.Forwarder;

public enum ForwarderRetryExhaustedPolicy
{
    DeadLetter,
    Discard
}

public sealed record ForwarderRetryConfiguration
{
    public int MaxAttempts { get; init; } = 10;
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(5);
    public ForwarderRetryExhaustedPolicy ExhaustedPolicy { get; init; } = ForwarderRetryExhaustedPolicy.DeadLetter;
}
