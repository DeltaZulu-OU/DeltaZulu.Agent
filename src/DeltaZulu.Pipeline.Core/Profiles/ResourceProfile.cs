namespace DeltaZulu.Pipeline.Core.Profiles;

public sealed class ResourceProfile
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public bool Enabled { get; set; } = true;
    public bool Mandatory { get; set; } = true;
    public ResourceDescriptor Resource { get; set; } = new();
    public ResourceInputContract Input { get; set; } = new();
    public ResourceOutputContract Output { get; set; } = new();
    public ResourceFilter Filter { get; set; } = new();
    public ResourceCondition? Condition { get; set; }
}

public sealed class ResourceCondition
{
    public string Type { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public bool Mandatory { get; set; } = true;
    public string? ScopePath { get; set; }
}

public sealed class ResourceDescriptor
{
    public string Platform { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string? Service { get; set; }
    public string? Channel { get; set; }
    public string? Session { get; set; }
    public string? Provider { get; set; }
    public Guid? ProviderGuid { get; set; }
    public string Mode { get; set; } = "attach";
    public string? Scope { get; set; }
    public List<string> RecordTypes { get; set; } = [];

    /// <summary>
    /// Opaque, family-specific resource knobs (e.g. ETW event/opcode filters). Core does not
    /// interpret these; each input family adapts its own slice via an
    /// <c>IResourceOptionsAdapter&lt;TOptions&gt;</c> implementation.
    /// </summary>
    public Dictionary<string, object?> Options { get; set; } = [];
}

public sealed class ResourceInputContract
{
    /// <summary>
    /// Logical KQL source name. Empty means the YAML loader must apply the family default.
    /// </summary>
    public string Table { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
}

public sealed class ResourceOutputContract
{
    public string Mode { get; set; } = "FilterAndSelect";
    public string Format { get; set; } = "ndjson";
    public bool PreserveOriginalFieldNames { get; set; } = true;
    public bool PreserveRawEvent { get; set; } = true;
    public bool MetadataEnvelope { get; set; } = true;
    public bool EventEnvelope { get; set; } = true;
    public string OnNoMatch { get; set; } = "drop";
}

public sealed class ResourceFilter
{
    public string Language { get; set; } = "kql";
    public string Query { get; set; } = string.Empty;
}