namespace DeltaZulu.Agent.Runtime;

public sealed record AgentRuntimeResult(bool Success, Exception? Error = null, IReadOnlyList<string>? Warnings = null)
{
    public bool Degraded => Success && Warnings is { Count: > 0 };
}