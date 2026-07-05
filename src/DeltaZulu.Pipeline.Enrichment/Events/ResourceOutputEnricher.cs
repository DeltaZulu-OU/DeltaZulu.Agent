using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Enrichment;

public static class ResourceOutputEnricher
{
    public static ResourceOutputRecord EnrichAfterFilter(ResourceOutputRecord record)
    {
        if (record.Enrichment is not null)
        {
            return record;
        }

        var enrichment = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        Add(enrichment, EtwEventEnricher.BuildEtwEnrichment(record.Event, record.Metadata));
        Add(enrichment, RpcEventEnricher.BuildRpcEnrichment(record.Event, record.Metadata));

        return enrichment.Count == 0 ? record : record with { Enrichment = enrichment };
    }

    private static void Add(IDictionary<string, object?> target, IReadOnlyDictionary<string, object?>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var item in source)
        {
            target[item.Key] = item.Value;
        }
    }
}
