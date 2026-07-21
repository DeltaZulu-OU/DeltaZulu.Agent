namespace DeltaZulu.LocalStream;

/// <summary>
/// Scaffold anchor for the LocalStream durability/replay host described in
/// ADR 0008 and ARCHITECTURE.md. The producer/subscription runtime, stream
/// positions, and checkpoints are ROADMAP.md Phase 9 work; this
/// type exists only so ROADMAP.md Phase 1 ("Pipeline references Normalize,
/// LocalStream, and FORWARDER") has a stable, reflectable assembly boundary before
/// that work lands. The two topic names are already decided by
/// ARCHITECTURE.md and are exposed here so later phases have one canonical
/// source for them instead of repeating string literals.
/// </summary>
public static class LocalStreamTopics
{
    /// <summary>Materialization-to-filter boundary: parsed envelopes.</summary>
    public const string Parsed = "agent.parsed";

    /// <summary>Filter-to-forwarder boundary: accepted output rows.</summary>
    public const string Output = "agent.output";
}
