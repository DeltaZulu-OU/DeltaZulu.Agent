using DeltaZulu.Agent.Domain.Delivery;
using DeltaZulu.Agent.Shared.MessagePack;

namespace DeltaZulu.Agent.Outputs.MessagePack;

/// <summary>
/// Encodes delivery batches as MessagePack for MessagePack-capable outputs.
/// </summary>
public sealed class MessagePackDeliveryBatchEncoder
{
    private readonly MessagePackPayloadSerializer _serializer;

    public MessagePackDeliveryBatchEncoder(MessagePackPayloadSerializer? serializer = null) =>
        _serializer = serializer ?? new MessagePackPayloadSerializer();

    public byte[] Encode(DeliveryBatch batch) => _serializer.Serialize(batch);
}
