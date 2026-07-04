namespace DeltaZulu.Pipeline.Enrichment.Windows;

public sealed record WindowsSidObservationOptions
{
    public TimeSpan CacheTtl { get; init; } = TimeSpan.FromDays(14);
    public IReadOnlyList<int> PriorityEventIds { get; init; } = [4720, 4726, 4732, 4733, 4624, 4672, 4688];
}