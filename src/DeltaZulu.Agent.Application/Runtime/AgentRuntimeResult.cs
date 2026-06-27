namespace DeltaZulu.Agent.Application.Runtime;

public sealed record AgentRuntimeResult(bool Success, Exception? Error = null);
