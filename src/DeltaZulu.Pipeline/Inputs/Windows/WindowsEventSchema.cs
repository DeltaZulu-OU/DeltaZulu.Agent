namespace DeltaZulu.Pipeline.Inputs.Windows;

/// <summary>
/// Structural schema for one Windows event: the channel/provider it belongs to and the native
/// fields it declares. This is collected dynamically from the host's provider metadata — it is not
/// a maintained data catalog and carries no event descriptions or value lookups.
/// </summary>
public sealed record WindowsEventSchema
{
    public required string Provider { get; init; }
    public required int EventId { get; init; }

    /// <summary>The EventLog channel this event publishes to, when the manifest declares one.
    /// Null for pure-ETW events that have no channel link.</summary>
    public string? Channel { get; init; }

    public IReadOnlyList<WindowsEventFieldSchema> Fields { get; init; } = [];
}

/// <summary>A native event field and its manifest input type (e.g. "win:SID").</summary>
public sealed record WindowsEventFieldSchema
{
    public required string Name { get; init; }
    public string? Type { get; init; }
}
