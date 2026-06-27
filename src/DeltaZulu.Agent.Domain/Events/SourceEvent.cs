namespace DeltaZulu.Agent.Core.Events;

public sealed record SourceEvent(
    ResourceMetadata Metadata,
    IReadOnlyDictionary<string, object?> Fields)
{
    public IDictionary<string, object?> ToKqlRow()
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in Fields)
        {
            row[field.Key] = field.Value;
        }

        if (!row.ContainsKey("source") && !string.IsNullOrWhiteSpace(Metadata.SourceName))
        {
            row["source"] = Metadata.SourceName;
        }

        row["_metadata"] = Metadata.ToDictionary();
        return row;
    }
}
