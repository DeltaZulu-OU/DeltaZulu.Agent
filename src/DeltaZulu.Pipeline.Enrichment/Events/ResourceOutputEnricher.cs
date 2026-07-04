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

        var enrichment = RpcEventEnricher.BuildRpcEnrichment(record.Event, record.Metadata);
        return enrichment is null ? record : record with { Enrichment = enrichment };
    }
}
