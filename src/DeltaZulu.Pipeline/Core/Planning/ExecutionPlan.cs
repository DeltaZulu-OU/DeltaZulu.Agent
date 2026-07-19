namespace DeltaZulu.Pipeline.Core.Planning;

public sealed record ExecutionPlan(IReadOnlyList<ResourceAcquisitionPlan> Acquisitions);
