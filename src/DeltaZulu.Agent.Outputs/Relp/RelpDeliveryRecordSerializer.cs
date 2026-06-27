using System.Text.Json;
using DeltaZulu.Agent.Domain.Delivery;
using DeltaZulu.DurableBuffer.Abstractions;

namespace DeltaZulu.Agent.Outputs.Relp;

public sealed class RelpDeliveryRecordSerializer : IRecordSerializer<DeliveryRecord>
{
    public ReadOnlyMemory<byte> Serialize(DeliveryRecord record) =>
        JsonSerializer.SerializeToUtf8Bytes(record, RelpOutputJson.Options);
}
