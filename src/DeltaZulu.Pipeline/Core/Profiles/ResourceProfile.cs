namespace DeltaZulu.Pipeline.Core.Profiles;

public sealed class ResourceCondition
{
    public bool Mandatory { get; set; } = true;
    public string Query { get; set; } = string.Empty;
    public string? ScopePath { get; set; }
    public string Type { get; set; } = string.Empty;
}

public sealed class ResourceDescriptor
{
    public string? Channel { get; set; }
    public string Family { get; set; } = string.Empty;
    public string Mode { get; set; } = "attach";

    /// <summary>
    /// Opaque, family-specific resource knobs (e.g. ETW event/opcode filters). Core does not
    /// interpret these; each input family adapts its own slice via an
    /// <c>IResourceOptionsAdapter&lt;TOptions&gt;</c> implementation.
    /// </summary>
    public Dictionary<string, object?> Options { get; set; } = [];

    public string Platform { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public Guid? ProviderGuid { get; set; }
    public List<string> RecordTypes { get; set; } = [];
    public string? Scope { get; set; }
    public string? Service { get; set; }
    public string? Session { get; set; }
}

public sealed class ResourceInputContract
{
    public string Schema { get; set; } = string.Empty;

    /// <summary>
    /// Logical KQL source name. Empty means the YAML loader must apply the family default.
    /// </summary>
    public string Table { get; set; } = string.Empty;
}

public sealed class ResourceOutputContract
{
    public bool EventEnvelope { get; set; } = true;
    public string Format { get; set; } = "ndjson";
    public bool MetadataEnvelope { get; set; } = true;
    public string Mode { get; set; } = "FilterAndSelect";
    public string OnNoMatch { get; set; } = "drop";
    public bool PreserveOriginalFieldNames { get; set; } = true;
    public bool PreserveRawEvent { get; set; } = true;
}

public sealed class ResourceProfile
{
    public ResourceCondition? Condition { get; set; }
    public bool Enabled { get; set; } = true;
    public ResourceFilter Filter { get; set; } = new();
    public string Id { get; set; } = string.Empty;
    public ResourceInputContract Input { get; set; } = new();
    public bool Mandatory { get; set; } = true;
    public string Name { get; set; } = string.Empty;
    public ResourceOutputContract Output { get; set; } = new();
    public ResourceDescriptor Resource { get; set; } = new();
    public int SchemaVersion { get; set; } = 1;
    public string Version { get; set; } = "1.0.0";
}

public sealed class ResourceFilter
{
    public string Language { get; set; } = "kql";
    public string Query { get; set; } = string.Empty;
}
