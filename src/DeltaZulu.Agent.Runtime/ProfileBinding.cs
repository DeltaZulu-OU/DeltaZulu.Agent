using DeltaZulu.Agent.Pipeline.Abstractions;
using DeltaZulu.Agent.Pipeline.Profiles;

namespace DeltaZulu.Agent.Runtime;

public sealed record ProfileBinding(
    ISourceInput Input,
    ResourceProfile Profile,
    IProfileExecutor Executor);
