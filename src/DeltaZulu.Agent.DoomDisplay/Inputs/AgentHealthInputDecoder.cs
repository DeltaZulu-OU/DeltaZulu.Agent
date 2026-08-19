using DeltaZulu.Forward;
using MessagePack;

namespace DeltaZulu.Agent.DoomDisplay.Inputs;

public interface IAgentHealthInputDecoder
{
    AgentHealthSnapshot Decode(ReadOnlyMemory<byte> payload);
}

/// <summary>
/// Decodes the payload of a DeltaZulu.Forward TypedBatch frame into an agent health reading.
/// Unlike the display stream, this payload is the protocol's own typed contract, so it is decoded
/// with <see cref="ForwardLogBatchCodec" /> rather than a private codec.
/// </summary>
public sealed class AgentHealthInputDecoder : IAgentHealthInputDecoder
{
    public AgentHealthSnapshot Decode(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            throw new AgentHealthDecodeException(
                "Agent-health payload is empty.",
                new InvalidDataException("Agent-health payload is empty."));
        }

        try
        {
            return AgentHealthCodec.FromBatch(ForwardLogBatchCodec.Decode(payload));
        }
        catch (Exception exception) when (exception is InvalidDataException or MessagePackSerializationException)
        {
            throw new AgentHealthDecodeException(exception.Message, exception);
        }
    }
}

public sealed class AgentHealthDecodeException(string message, Exception innerException)
    : Exception(message, innerException);
