namespace DeltaZulu.Agent.Security.EtwIntegrity;

public sealed class EtwIntegrityOptions
{
    public int PrologueSize { get; init; } = 16;

    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromSeconds(5);

    public IReadOnlyList<string> TargetFunctions { get; init; } =
    [
        "EtwEventWrite",
        "NtTraceEvent"
    ];

    public int ConsecutiveReadFailuresBeforeDisable { get; init; } = 3;

    public void Validate()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ETW integrity checking is Windows-only.");
        }

        ValidatePortable();
    }

    internal void ValidatePortable()
    {
        if (PrologueSize < 6)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PrologueSize),
                "Prologue size must be at least 6 bytes.");
        }

        if (CheckInterval < TimeSpan.FromMilliseconds(250))
        {
            throw new ArgumentOutOfRangeException(
                nameof(CheckInterval),
                "Check interval is too aggressive.");
        }

        if (TargetFunctions.Count == 0)
        {
            throw new ArgumentException("At least one ETW target function is required.");
        }

        if (ConsecutiveReadFailuresBeforeDisable < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConsecutiveReadFailuresBeforeDisable),
                "At least one read failure is required before disabling a target.");
        }
    }
}
