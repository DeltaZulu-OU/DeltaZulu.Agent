using System.Text.Json;
using DeltaZulu.DurableBuffer.Abstractions;
using DeltaZulu.Pipeline.Core.Delivery;

namespace DeltaZulu.Pipeline.Outputs.Forwarder;

public sealed class ForwarderDeliveryRecordSerializer : IRecordSerializer<DeliveryRecord>
{
    public ReadOnlyMemory<byte> Serialize(DeliveryRecord record) =>
        JsonSerializer.SerializeToUtf8Bytes(record, ForwarderOutputJson.Options);
}
