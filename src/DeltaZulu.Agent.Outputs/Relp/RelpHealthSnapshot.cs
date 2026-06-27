using DeltaZulu.DurableBuffer.Abstractions;

namespace DeltaZulu.Agent.Outputs.Relp;

public sealed record RelpHealthSnapshot
{
    public required BufferSnapshot Buffer { get; init; }
    public DateTimeOffset? LastForwarderActivityUtc { get; init; }
}
