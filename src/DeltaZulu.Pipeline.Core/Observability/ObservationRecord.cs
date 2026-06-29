namespace DeltaZulu.Pipeline.Core.Observability;

internal static class ObservationRecord
{
    public static IReadOnlyDictionary<string, object?> MetadataWithKind(CollectorObservationMetadata metadata, string recordKind)
    {
        var copy = metadata.ToDictionary();
        copy.EnsureCapacity(copy.Count + 1);
        copy["recordKind"] = recordKind;
        return copy;
    }
}