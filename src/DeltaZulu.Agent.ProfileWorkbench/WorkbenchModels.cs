using DeltaZulu.Agent.SchemaMetadata;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Agent.ProfileWorkbench;

public enum WorkbenchRunMode
{
    Replay,
    Follow
}

public sealed record WorkbenchSourceCandidate(
    string SourceKind,
    string DisplayName,
    string Table,
    string? PathOrResource,
    SchemaDescriptor Schema,
    bool RequiresBinding);

public sealed record BoundWorkbenchSource(
    string SourceKind,
    string DisplayName,
    string Table,
    SchemaDescriptor Schema,
    ISourceInput Input);

public sealed record WorkbenchRunRequest(
    ResourceProfileDocument Document,
    BoundWorkbenchSource Source,
    string Query,
    int RowLimit,
    WorkbenchRunMode Mode);

public sealed record WorkbenchRunResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    WorkbenchCounters Counters,
    string? Error = null,
    bool Truncated = false);

public sealed record WorkbenchCounters(
    long Read,
    long Matched,
    long Errors,
    long Displayed,
    DateTimeOffset? LastEventUtc);

public sealed record WorkbenchRow(ResourceOutputRecord Record);
