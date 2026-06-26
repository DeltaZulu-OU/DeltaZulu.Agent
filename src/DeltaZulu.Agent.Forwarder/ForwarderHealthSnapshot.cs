using DeltaZulu.Buffer.Abstractions;

namespace DeltaZulu.Agent.Forwarder;

public sealed record ForwarderHealthSnapshot
{
    public required BufferSnapshot Buffer { get; init; }
    public DateTimeOffset? LastForwarderActivityUtc { get; init; }
}
