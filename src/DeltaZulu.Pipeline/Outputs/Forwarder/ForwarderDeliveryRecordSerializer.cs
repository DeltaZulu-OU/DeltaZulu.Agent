using System.Text.Json;
using DeltaZulu.DurableBuffer.Abstractions;
using DeltaZulu.Forward;

namespace DeltaZulu.Pipeline.Outputs.Forwarder;

public sealed class ForwarderDeliveryRecordSerializer : IRecordSerializer<ForwardLogRecord>
{
    public ReadOnlyMemory<byte> Serialize(ForwardLogRecord record) =>
        JsonSerializer.SerializeToUtf8Bytes(record, ForwarderOutputJson.Options);
}
