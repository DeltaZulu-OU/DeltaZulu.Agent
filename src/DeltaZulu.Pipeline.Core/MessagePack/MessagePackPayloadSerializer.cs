using MessagePack;

namespace DeltaZulu.Pipeline.Core.MessagePack;

/// <summary>
/// Shared MessagePack payload serializer for input/output projects that need the same wire options.
/// </summary>
public sealed class MessagePackPayloadSerializer
{
    private readonly MessagePackSerializerOptions _options;

    public MessagePackPayloadSerializer(MessagePackSerializerOptions? options = null)
    {
        _options = options ?? AgentMessagePackSerializerOptions.CreateDefault();
    }

    public byte[] Serialize<T>(T value)
    {
        try
        {
            return MessagePackSerializer.Serialize(MessagePackValueNormalizer.Normalize(value), _options);
        }
        catch (MessagePackSerializationException ex)
        {
            throw new MessagePackSerializationException($"Failed to MessagePack serialize {typeof(T).FullName}: {ex.Message}", ex);
        }
    }

    public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
    {
        try
        {
            var value = MessagePackSerializer.Deserialize<T>(payload, _options);
            return value is null ? default : MessagePackValueNormalizer.Normalize(value);
        }
        catch (MessagePackSerializationException ex)
        {
            throw new MessagePackSerializationException($"Failed to MessagePack deserialize {typeof(T).FullName} from {payload.Length} bytes: {ex.Message}", ex);
        }
    }
}
