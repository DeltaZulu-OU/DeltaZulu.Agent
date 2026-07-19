using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Core.Inputs;

/// <summary>
/// Deterministically decoded structured input that bypasses Normalize and is
/// later checked against the producer-agnostic schema registry.
/// </summary>
public sealed record StructuredInputRecord(
    ResourceMetadata Metadata,
    IReadOnlyDictionary<string, object?> Fields,
    string PayloadFormat);
