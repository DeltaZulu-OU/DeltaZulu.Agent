namespace DeltaZulu.Agent.Filter.Kql;

/// <summary>
/// Identifies the shared Agent/Rx.Kql query-execution step that failed.
/// </summary>
public enum KqlQueryFailureStage
{
    ShimNormalization,
    RxKqlParsing,
    RxKqlExecution
}

/// <summary>
/// Adds profile and query context to failures raised while executing a KQL query.
/// Query text is retained as structured diagnostic data instead of being included in
/// the message, so callers can make an explicit safe-to-display decision.
/// </summary>
public sealed class KqlQueryExecutionException : Exception
{
    public KqlQueryExecutionException(
        string profileId,
        KqlQueryFailureStage stage,
        string originalQuery,
        string? normalizedQuery,
        Exception innerException)
        : base($"KQL query failed for profile '{profileId}' during {FormatStage(stage)}.", innerException)
    {
        ProfileId = profileId;
        Stage = stage;
        OriginalQuery = originalQuery;
        NormalizedQuery = normalizedQuery;
    }

    public string ProfileId { get; }
    public KqlQueryFailureStage Stage { get; }
    public string OriginalQuery { get; }
    public string? NormalizedQuery { get; }

    private static string FormatStage(KqlQueryFailureStage stage) => stage switch {
        KqlQueryFailureStage.ShimNormalization => "Agent shim normalization",
        KqlQueryFailureStage.RxKqlParsing => "Rx.Kql parsing",
        KqlQueryFailureStage.RxKqlExecution => "Rx.Kql execution",
        _ => stage.ToString()
    };
}
