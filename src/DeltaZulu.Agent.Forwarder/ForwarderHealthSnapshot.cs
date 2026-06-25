using DeltaZulu.Buffer.Abstractions;

namespace DeltaZulu.Agent.Forwarder;

public sealed record ForwarderHealthSnapshot
{
    public required BufferSnapshot Buffer { get; init; }
    public long BatchesSentTotal { get; init; }
    public long BatchesAcknowledgedTotal { get; init; }
    public long BatchesFailedTotal { get; init; }
    public long BatchesRetryScheduledTotal { get; init; }
    public long BatchesDeadLetteredTotal { get; init; }
    public DateTimeOffset? LastForwarderActivityUtc { get; init; }
}
