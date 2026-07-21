using DeltaZulu.DurableBuffer.Abstractions;

namespace DeltaZulu.Pipeline.Outputs.Forwarder;

public sealed record ForwarderHealthSnapshot
{
    public required BufferSnapshot Buffer { get; init; }
    public ForwarderTransportSnapshot? Transport { get; init; }
    public DateTimeOffset? LastForwarderActivityUtc { get; init; }
}
