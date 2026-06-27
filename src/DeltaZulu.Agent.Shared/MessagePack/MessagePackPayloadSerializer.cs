using MessagePack;

namespace DeltaZulu.Agent.Shared.MessagePack;

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

    public byte[] Serialize<T>(T value) => MessagePackSerializer.Serialize(value, _options);

    public T? Deserialize<T>(ReadOnlyMemory<byte> payload) => MessagePackSerializer.Deserialize<T>(payload, _options);
}
