namespace DeltaZulu.Agent.Shared.Orchestrator.Runtime;

public sealed record AgentRuntimeResult(bool Success, Exception? Error = null);
