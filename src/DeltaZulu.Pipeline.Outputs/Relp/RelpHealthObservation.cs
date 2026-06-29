using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Observability;

namespace DeltaZulu.Pipeline.Outputs.Relp;

public sealed record RelpHealthObservation
{
    public const string RecordKind = "collector.forwarder.health";

    public required CollectorObservationMetadata Metadata { get; init; }
    public required RelpHealthSnapshot Health { get; init; }

    public ResourceOutputRecord ToOutputRecord()
    {
        var metadata = Metadata.ToDictionary();
        metadata.EnsureCapacity(metadata.Count + 1);
        metadata["recordKind"] = RecordKind;

        return new ResourceOutputRecord {
            Metadata = metadata,
            Event = new Dictionary<string, object?>(17, StringComparer.OrdinalIgnoreCase) {
                ["bufferState"] = Health.Buffer.State.ToString(),
                ["diskBytesUsed"] = Health.Buffer.DiskBytesUsed,
                ["diskBytesLimit"] = Health.Buffer.DiskBytesLimit,
                ["memoryBytesUsed"] = Health.Buffer.MemoryBytesUsed,
                ["openChunkBytes"] = Health.Buffer.OpenChunkBytes,
                ["sealedChunkCount"] = Health.Buffer.SealedChunkCount,
                ["retryQueueDepth"] = Health.Buffer.RetryQueueDepth,
                ["oldestChunkAgeMs"] = Health.Buffer.OldestChunkAge?.TotalMilliseconds,
                ["recordsAcceptedTotal"] = Health.Buffer.RecordsAcceptedTotal,
                ["recordsRejectedTotal"] = Health.Buffer.RecordsRejectedTotal,
                ["recordsDroppedTotal"] = Health.Buffer.RecordsDroppedTotal,
                ["chunksDeliveredTotal"] = Health.Buffer.ChunksDeliveredTotal,
                ["chunksDeadLetteredTotal"] = Health.Buffer.ChunksDeadLetteredTotal,
                ["chunksSentTotal"] = Health.Buffer.ChunksSentTotal,
                ["chunksFailedTotal"] = Health.Buffer.ChunksFailedTotal,
                ["chunksRetryScheduledTotal"] = Health.Buffer.ChunksRetryScheduledTotal,
                ["lastForwarderActivityUtc"] = Health.LastForwarderActivityUtc
            }
        };
    }
}