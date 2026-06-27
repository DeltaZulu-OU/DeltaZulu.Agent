using System.Buffers;
using System.Text.Json;
using DeltaZulu.Agent.Pipeline.Abstractions;
using DeltaZulu.Agent.Pipeline.Delivery;
using DeltaZulu.DurableBuffer.Chunks;
using DeltaZulu.DurableBuffer.Dispatch;

namespace DeltaZulu.Agent.Outputs.Relp;

public sealed class RelpChunkSender : IChunkSender
{
    private readonly IDeliveryTransport _transport;

    public RelpChunkSender(IDeliveryTransport transport) => _transport = transport;

    public async ValueTask<ChunkSendResult> SendAsync(
        StoredChunk chunk,
        CancellationToken cancellationToken = default)
    {
        var fileLength = (int)new FileInfo(chunk.ChunkFilePath).Length;
        var chunkBytes = ArrayPool<byte>.Shared.Rent(fileLength);
        try
        {
            await using var stream = new FileStream(chunk.ChunkFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            await stream.ReadExactlyAsync(chunkBytes.AsMemory(0, fileLength), cancellationToken).ConfigureAwait(false);

            var records = new List<DeliveryRecord>();
            foreach (var recordBytes in ChunkFormat.ReadRecords(chunkBytes.AsMemory(0, fileLength)))
            {
                var record = JsonSerializer.Deserialize<DeliveryRecord>(recordBytes.Span, RelpOutputJson.Options);
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
        finally
        {
            ArrayPool<byte>.Shared.Return(chunkBytes);
        }
    }
}
