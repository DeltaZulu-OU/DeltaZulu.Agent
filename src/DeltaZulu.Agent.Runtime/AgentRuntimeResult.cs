namespace DeltaZulu.Agent.Runtime;

public sealed record AgentRuntimeResult(bool Success, Exception? Error = null);
