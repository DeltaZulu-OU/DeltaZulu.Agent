namespace DeltaZulu.Pipeline.Outputs.Relp;

public enum RelpRetryExhaustedPolicy
{
    DeadLetter,
    Discard
}

public sealed record RelpRetryConfiguration
{
    public int MaxAttempts { get; init; } = 10;
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(5);
    public RelpRetryExhaustedPolicy ExhaustedPolicy { get; init; } = RelpRetryExhaustedPolicy.DeadLetter;
}
