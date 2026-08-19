using MessagePack;

namespace DeltaZulu.Agent.DoomDisplay.Inputs;

public interface IDoomInputDecoder
{
    DoomFramePacket Decode(ReadOnlyMemory<byte> payload);
}

/// <summary>
/// Decodes the application payload of a DeltaZulu.Forward RawEnvelope into a validated Doom frame.
/// Transport framing, integrity checks, and acknowledgment flow remain the responsibility of DeltaZulu.Forward.
/// </summary>
public sealed class DoomInputDecoder : IDoomInputDecoder
{
    public DoomFramePacket Decode(ReadOnlyMemory<byte> payload)
    {
        try
        {
            return DoomFrameCodec.Decode(payload);
        }
        catch (Exception exception) when (exception is InvalidDataException or MessagePackSerializationException)
        {
            throw new DoomInputDecodeException(exception.Message, exception);
        }
    }
}

public sealed class DoomInputDecodeException(string message, Exception innerException)
    : Exception(message, innerException);
