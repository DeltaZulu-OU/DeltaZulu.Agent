using System.Text.Json;
using DeltaZulu.Buffer.Chunks;
using DeltaZulu.Buffer.Dispatch;

namespace DeltaZulu.Agent.Forwarder;

public sealed class ForwarderChunkSender : IChunkSender
{
    private readonly IForwarderTransport _transport;

    public ForwarderChunkSender(IForwarderTransport transport) => _transport = transport;

    public async ValueTask<ChunkSendResult> SendAsync(
        StoredChunk chunk,
        CancellationToken cancellationToken = default)
    {
        var chunkBytes = await File.ReadAllBytesAsync(chunk.ChunkFilePath, cancellationToken);
        var records = new List<DeliveryRecord>();
        foreach (var recordBytes in ChunkFormat.ReadRecords(chunkBytes))
        {
            var record = JsonSerializer.Deserialize<DeliveryRecord>(recordBytes.ToArray(), ForwarderJson.Options);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        if (records.Count != chunk.Metadata.RecordCount)
        {
            return new ChunkSendResult(
                ChunkSendStatus.PermanentFailure,
                $"Chunk {chunk.Id} expected {chunk.Metadata.RecordCount} records but decoded {records.Count}.");
        }

        var ack = await _transport.SendAsync(new DeliveryBatch
        {
            BatchId = chunk.Id.Value,
            CreatedAt = chunk.Metadata.SealedUtc ?? chunk.Metadata.CreatedUtc,
            Records = records
        }, cancellationToken);

        return ack.Accepted
            ? new ChunkSendResult(ChunkSendStatus.Success)
            : new ChunkSendResult(ChunkSendStatus.TransientFailure, ack.Reason ?? "Forwarder batch was not acknowledged.");
    }
}
