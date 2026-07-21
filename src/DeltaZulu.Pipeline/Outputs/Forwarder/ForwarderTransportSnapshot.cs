namespace DeltaZulu.Pipeline.Outputs.Forwarder;

public sealed record ForwarderTransportSnapshot
{
    public required long SendAttemptsTotal { get; init; }
    public required long SendSuccessesTotal { get; init; }
    public required long TransientFailuresTotal { get; init; }
    public required long PermanentFailuresTotal { get; init; }
    public required long ChunksDeadLetteredTotal { get; init; }
    public required long ChunksDiscardedTotal { get; init; }
    public required bool IsRunning { get; init; }
}
