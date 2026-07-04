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

        var eventFields = new Dictionary<string, object?>(24, StringComparer.OrdinalIgnoreCase) {
            ["bufferState"] = Health.Buffer.State.ToString(),
            ["diskBytesUsed"] = Health.Buffer.DiskBytesUsed,
            ["diskBytesLimit"] = Health.Buffer.DiskBytesLimit,
            ["memoryBytesUsed"] = Health.Buffer.MemoryBytesUsed,
            ["openChunkBytes"] = Health.Buffer.OpenChunkBytes,
            ["sealedChunkCount"] = Health.Buffer.SealedChunkCount,
            ["oldestChunkAgeMs"] = Health.Buffer.OldestChunkAge?.TotalMilliseconds,
            ["recordsAcceptedTotal"] = Health.Buffer.RecordsAcceptedTotal,
            ["recordsRejectedTotal"] = Health.Buffer.RecordsRejectedTotal,
            ["recordsDroppedTotal"] = Health.Buffer.RecordsDroppedTotal,
            ["chunksCompletedTotal"] = Health.Buffer.ChunksCompletedTotal,
            ["chunksReleasedTotal"] = Health.Buffer.ChunksReleasedTotal,
            ["chunksDeadLetteredTotal"] = Health.Buffer.ChunksDeadLetteredTotal,
            ["lastForwarderActivityUtc"] = Health.LastForwarderActivityUtc
        };

        if (Health.Transport is { } transport)
        {
            eventFields["transportSendAttemptsTotal"] = transport.SendAttemptsTotal;
            eventFields["transportSendSuccessesTotal"] = transport.SendSuccessesTotal;
            eventFields["transportTransientFailuresTotal"] = transport.TransientFailuresTotal;
            eventFields["transportPermanentFailuresTotal"] = transport.PermanentFailuresTotal;
            eventFields["transportChunksDeadLetteredTotal"] = transport.ChunksDeadLetteredTotal;
            eventFields["transportChunksDiscardedTotal"] = transport.ChunksDiscardedTotal;
            eventFields["transportIsRunning"] = transport.IsRunning;
        }

        return new ResourceOutputRecord {
            Metadata = metadata,
            Event = eventFields
        };
    }
}