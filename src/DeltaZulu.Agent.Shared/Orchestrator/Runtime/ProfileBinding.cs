using DeltaZulu.Agent.Shared.Pipeline.Abstractions;
using DeltaZulu.Agent.Shared.Pipeline.Profiles;

namespace DeltaZulu.Agent.Shared.Orchestrator.Runtime;

public sealed record ProfileBinding(
    ISourceInput Input,
    ResourceProfile Profile,
    IProfileExecutor Executor);
