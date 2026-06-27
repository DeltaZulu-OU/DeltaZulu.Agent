using DeltaZulu.Agent.Application.Abstractions;
using DeltaZulu.Agent.Profiles;

namespace DeltaZulu.Agent.Application.Runtime;

public sealed record ProfileBinding(
    ISourceInput Input,
    ResourceProfile Profile,
    IProfileExecutor Executor);
