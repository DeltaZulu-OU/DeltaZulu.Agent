namespace DeltaZulu.Agent.Pipeline.Ndjson;

public sealed record NdjsonErrorRecord
{
    public string Type { get; init; } = "agent.error";
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Message { get; init; } = string.Empty;
    public string? ExceptionType { get; init; }
    public string? StackTrace { get; init; }

    public static NdjsonErrorRecord FromException(Exception exception) => new()
    {
        Message = exception.Message,
        ExceptionType = exception.GetType().FullName,
        StackTrace = exception.StackTrace
    };
}