using DeltaZulu.Agent.Shared.Pipeline.Delivery;
using DeltaZulu.Agent.Shared.Pipeline.MessagePack;

namespace DeltaZulu.Agent.Inputs.MessagePack;

/// <summary>
/// Decodes MessagePack delivery batches for MessagePack-capable inputs.
/// </summary>
public sealed class MessagePackDeliveryBatchDecoder
{
    private readonly MessagePackPayloadSerializer _serializer;

    public MessagePackDeliveryBatchDecoder(MessagePackPayloadSerializer? serializer = null) =>
        _serializer = serializer ?? new MessagePackPayloadSerializer();

    public DeliveryBatch? Decode(ReadOnlyMemory<byte> payload) =>
        _serializer.Deserialize<DeliveryBatch>(payload);
}
