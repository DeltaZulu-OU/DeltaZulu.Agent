namespace DeltaZulu.Pipeline.Inputs.Etw;

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
        var fields = new Dictionary<string, object?>(21, StringComparer.OrdinalIgnoreCase);
        AddTo(fields);
        return fields;
    }

    public void AddTo(IDictionary<string, object?> fields)
    {
        fields[nameof(ProviderGuid)] = ProviderGuid;
        fields[nameof(ProviderName)] = ProviderName;
        fields[nameof(EventId)] = EventId;
        fields["EventName"] = null;
        fields[nameof(Opcode)] = Opcode;
        fields[nameof(OpcodeName)] = OpcodeName;
        fields[nameof(Version)] = Version;
        fields[nameof(Task)] = Task;
        fields[nameof(TaskName)] = TaskName;
        fields["LevelCode"] = Level;
        fields["Level"] = LevelName;
        fields[nameof(Keywords)] = Keywords;
        fields["TimeStamp"] = TimestampUtc;
        fields[nameof(TimestampRaw)] = TimestampRaw;
        fields[nameof(ProcessId)] = ProcessId;
        fields[nameof(ThreadId)] = ThreadId;
        fields[nameof(ActivityId)] = ActivityId;
        fields[nameof(RelatedActivityId)] = RelatedActivityId;
        fields[nameof(Channel)] = Channel;
        fields[nameof(ProcessorId)] = ProcessorId;
        fields[nameof(PayloadLength)] = PayloadLength;
    }
}

public sealed class NativeEtwIdentityFilter
{
    public IReadOnlySet<int> EventIds { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> ExcludedEventIds { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> Opcodes { get; init; } = new HashSet<int>();
    public Guid? ProviderGuid { get; init; }
    public string? ProviderName { get; init; }
    public long? RequiredKeywords { get; init; }
    public IReadOnlySet<int> Versions { get; init; } = new HashSet<int>();

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

        if (ExcludedEventIds.Contains(envelope.EventId))
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

    public bool Matches(
        string providerName,
        Guid providerGuid,
        int eventId,
        int opcode,
        int version,
        long keywords)
    {
        if (!string.IsNullOrWhiteSpace(ProviderName) &&
            !providerName.Equals(ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ProviderGuid.HasValue && providerGuid != ProviderGuid.Value)
        {
            return false;
        }

        if (EventIds.Count > 0 && !EventIds.Contains(eventId))
        {
            return false;
        }

        if (ExcludedEventIds.Contains(eventId))
        {
            return false;
        }

        if (Opcodes.Count > 0 && !Opcodes.Contains(opcode))
        {
            return false;
        }

        if (Versions.Count > 0 && !Versions.Contains(version))
        {
            return false;
        }

        return !RequiredKeywords.HasValue || (keywords & RequiredKeywords.Value) != 0;
    }
}
