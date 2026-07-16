using System.Text.Json;
using DeltaZulu.DurableBuffer.Abstractions;
using DeltaZulu.Pipeline.Core.Delivery;

namespace DeltaZulu.Pipeline.Outputs.Relp;

public sealed class RelpDeliveryRecordSerializer : IRecordSerializer<DeliveryRecord>
{
    public ReadOnlyMemory<byte> Serialize(DeliveryRecord record) =>
        JsonSerializer.SerializeToUtf8Bytes(record, RelpOutputJson.Options);
}
