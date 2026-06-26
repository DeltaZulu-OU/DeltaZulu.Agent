using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Agent.Core.Observability;

namespace DeltaZulu.Agent.Forwarder;

public sealed record ForwarderHealthObservation
{
    public const string RecordKind = "collector.forwarder.health";

    public required CollectorObservationMetadata Metadata { get; init; }
    public required ForwarderHealthSnapshot Health { get; init; }

    public ResourceOutputRecord ToOutputRecord()
    {
        var metadata = new Dictionary<string, object?>(Metadata.ToDictionary(), StringComparer.OrdinalIgnoreCase)
        {
            ["recordKind"] = RecordKind
        };

        return new ResourceOutputRecord
        {
            Metadata = metadata,
            Event = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
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
                ["batchesSentTotal"] = Health.Buffer.ChunksSentTotal,
                ["batchesAcknowledgedTotal"] = Health.Buffer.ChunksDeliveredTotal,
                ["batchesFailedTotal"] = Health.Buffer.ChunksFailedTotal,
                ["batchesRetryScheduledTotal"] = Health.Buffer.ChunksRetryScheduledTotal,
                ["batchesDeadLetteredTotal"] = Health.Buffer.ChunksDeadLetteredTotal,
                ["lastForwarderActivityUtc"] = Health.LastForwarderActivityUtc
            }
        };
    }
}
