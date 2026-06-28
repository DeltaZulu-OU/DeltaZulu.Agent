namespace DeltaZulu.Pipeline.Core.Observability;

internal static class ObservationRecord
{
    public static IReadOnlyDictionary<string, object?> MetadataWithKind(CollectorObservationMetadata metadata, string recordKind)
    {
        var copy = new Dictionary<string, object?>(metadata.ToDictionary(), StringComparer.OrdinalIgnoreCase)
        {
            ["recordKind"] = recordKind
        };
        return copy;
    }
}
