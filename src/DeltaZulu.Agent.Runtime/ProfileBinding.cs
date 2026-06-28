using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Agent.Runtime;

public sealed record ProfileBinding(
    ISourceInput Input,
    ResourceProfile Profile,
    IProfileExecutor Executor,
    ProfileReloadSource? ProfileReloads = null)
{
    public ProfileBinding(ISourceInput input, ResourceProfile profile, IProfileExecutor executor)
        : this(input, profile, executor, null)
    {
    }
}
