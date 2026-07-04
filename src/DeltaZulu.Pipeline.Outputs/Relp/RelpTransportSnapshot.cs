namespace DeltaZulu.Pipeline.Outputs.Relp;

public sealed record RelpTransportSnapshot
{
    public required long SendAttemptsTotal { get; init; }
    public required long SendSuccessesTotal { get; init; }
    public required long TransientFailuresTotal { get; init; }
    public required long PermanentFailuresTotal { get; init; }
    public required long ChunksDeadLetteredTotal { get; init; }
    public required long ChunksDiscardedTotal { get; init; }
    public required bool IsRunning { get; init; }
}
