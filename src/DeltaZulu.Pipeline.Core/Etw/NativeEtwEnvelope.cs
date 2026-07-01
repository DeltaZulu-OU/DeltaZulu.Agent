namespace DeltaZulu.Pipeline.Core.Etw;

public sealed record NativeEtwEnvelope
{
    public required string ProviderName { get; init; }
    public required Guid ProviderGuid { get; init; }
    public int EventId { get; init; }
    public int Opcode { get; init; }
    public string? OpcodeName { get; init; }
    public int Version { get; init; }
    public long Keywords { get; init; }
    public int Level { get; init; }
    public string? LevelName { get; init; }
    public int Task { get; init; }
    public string? TaskName { get; init; }
    public int? Channel { get; init; }
    public int? ProcessorId { get; init; }
    public DateTime TimestampUtc { get; init; }
    public long? TimestampRaw { get; init; }
    public int ProcessId { get; init; }
    public int ThreadId { get; init; }
    public Guid ActivityId { get; init; }
    public Guid? RelatedActivityId { get; init; }
    public int? PayloadLength { get; init; }

    public IReadOnlyDictionary<string, object?> ToDictionary()
    {
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(ProviderGuid)] = ProviderGuid,
            [nameof(ProviderName)] = ProviderName,
            [nameof(EventId)] = EventId,
            ["EventName"] = null,
            [nameof(Opcode)] = Opcode,
            [nameof(OpcodeName)] = OpcodeName,
            [nameof(Version)] = Version,
            [nameof(Task)] = Task,
            [nameof(TaskName)] = TaskName,
            ["LevelCode"] = Level,
            ["Level"] = LevelName,
            [nameof(Keywords)] = Keywords,
            ["TimeStamp"] = TimestampUtc,
            [nameof(TimestampRaw)] = TimestampRaw,
            [nameof(ProcessId)] = ProcessId,
            [nameof(ThreadId)] = ThreadId,
            [nameof(ActivityId)] = ActivityId,
            [nameof(RelatedActivityId)] = RelatedActivityId,
            [nameof(Channel)] = Channel,
            [nameof(ProcessorId)] = ProcessorId,
            [nameof(PayloadLength)] = PayloadLength
        };

        return fields;
    }
}

public sealed class NativeEtwIdentityFilter
{
    public string? ProviderName { get; init; }
    public Guid? ProviderGuid { get; init; }
    public IReadOnlySet<int> EventIds { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> Opcodes { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> Versions { get; init; } = new HashSet<int>();
    public long? RequiredKeywords { get; init; }

    public bool Matches(NativeEtwEnvelope envelope)
    {
        if (!string.IsNullOrWhiteSpace(ProviderName) &&
            !envelope.ProviderName.Equals(ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ProviderGuid.HasValue && envelope.ProviderGuid != ProviderGuid.Value)
        {
            return false;
        }

        if (EventIds.Count > 0 && !EventIds.Contains(envelope.EventId))
        {
            return false;
        }

        if (Opcodes.Count > 0 && !Opcodes.Contains(envelope.Opcode))
        {
            return false;
        }

        if (Versions.Count > 0 && !Versions.Contains(envelope.Version))
        {
            return false;
        }

        return !RequiredKeywords.HasValue || (envelope.Keywords & RequiredKeywords.Value) != 0;
    }
}
