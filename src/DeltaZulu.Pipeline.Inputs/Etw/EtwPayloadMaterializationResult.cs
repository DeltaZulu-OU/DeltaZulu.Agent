namespace DeltaZulu.Pipeline.Inputs.Etw;

public sealed record EtwPayloadMaterializationResult(
    IReadOnlySet<string> MaterializedPayloadFields,
    IReadOnlySet<string> FailedPayloadFields)
{
    public static EtwPayloadMaterializationResult Empty { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}
