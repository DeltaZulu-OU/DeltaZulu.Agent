using DeltaZulu.Agent.Core.Events;

namespace DeltaZulu.Agent.Core.Observability;

public sealed class AgentObservationAccumulator
{
    private readonly object _gate = new();
    private readonly Dictionary<LogTelemetryKey, MutableCounts> _counts = [];

    public void RecordRead(SourceEvent source) => Increment(LogTelemetryKey.FromSourceEvent(source), counts => counts.ReadCount++);
    public void RecordKeptAfterFilter(ResourceOutputRecord record) => Increment(LogTelemetryKey.FromOutputRecord(record), counts => counts.KeptAfterFilterCount++);
    public void RecordForwarded(ResourceOutputRecord record) => Increment(LogTelemetryKey.FromOutputRecord(record), counts => counts.ForwardedCount++);
    public void RecordForwardFailed(ResourceOutputRecord record) => Increment(LogTelemetryKey.FromOutputRecord(record), counts => counts.ForwardFailedCount++);

    public IReadOnlyList<PipelineCountsObservation> SnapshotPipelineCounts(CollectorObservationMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        lock (_gate)
        {
            return _counts
                .OrderBy(pair => pair.Key.SourceType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Key.Channel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Key.Provider, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Key.EventId)
                .Select(pair => pair.Value.ToObservation(pair.Key, metadata))
                .ToArray();
        }
    }

    private void Increment(LogTelemetryKey key, Action<MutableCounts> increment)
    {
        lock (_gate)
        {
            if (!_counts.TryGetValue(key, out var counts))
            {
                counts = new MutableCounts();
                _counts[key] = counts;
            }

            increment(counts);
        }
    }

    private sealed class MutableCounts
    {
        public long ReadCount { get; set; }
        public long KeptAfterFilterCount { get; set; }
        public long ForwardedCount { get; set; }
        public long ForwardFailedCount { get; set; }

        public PipelineCountsObservation ToObservation(LogTelemetryKey key, CollectorObservationMetadata metadata) => new()
        {
            LogKey = key,
            Metadata = metadata,
            ReadCount = ReadCount,
            KeptAfterFilterCount = KeptAfterFilterCount,
            DiscardedCount = Math.Max(0, ReadCount - KeptAfterFilterCount),
            ForwardedCount = ForwardedCount,
            ForwardFailedCount = ForwardFailedCount
        };
    }
}
