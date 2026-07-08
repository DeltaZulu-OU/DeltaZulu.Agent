namespace DeltaZulu.Agent.SchemaMetadata;

public sealed record SchemaDescriptor(
    string Table,
    IReadOnlyList<SchemaFieldDescriptor> Fields,
    string SourceKind,
    string Provenance,
    int Confidence,
    bool Executable);

public sealed record SchemaFieldDescriptor(
    string Name,
    string KqlType,
    string Origin,
    int Confidence,
    bool Nullable = true,
    bool Repeated = false,
    string? RawName = null,
    string? RawPath = null,
    string? Description = null);
