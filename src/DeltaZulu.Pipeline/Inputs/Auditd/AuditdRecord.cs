namespace DeltaZulu.Pipeline.Inputs.Auditd;

public sealed record AuditdRecord(
    string Id,
    string Type,
    IReadOnlyDictionary<string, object?> Fields,
    string RawLine);
