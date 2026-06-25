using System.Text.Json;
using DeltaZulu.Buffer.Abstractions;

namespace DeltaZulu.Agent.Forwarder;

public sealed class DeliveryRecordSerializer : IRecordSerializer<DeliveryRecord>
{
    public ReadOnlyMemory<byte> Serialize(DeliveryRecord record) =>
        JsonSerializer.SerializeToUtf8Bytes(record, ForwarderJson.Options);
}
