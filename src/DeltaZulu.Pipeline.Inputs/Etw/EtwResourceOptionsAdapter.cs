using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Pipeline.Inputs.Etw;

public sealed class EtwResourceOptionsAdapter : IResourceOptionsAdapter<EtwResourceOptions>
{
    public EtwResourceOptions Adapt(ResourceDescriptor resource) => new() {
        EventIds = resource.Options.GetIntList("eventIds"),
        ExcludedEventIds = resource.Options.GetIntList("excludedEventIds"),
        Opcodes = resource.Options.GetIntList("opcodes"),
        Versions = resource.Options.GetIntList("versions"),
        CaptureStacks = resource.Options.GetBool("captureStacks"),
        StackEventIds = resource.Options.GetIntList("stackEventIds"),
        ExcludedStackEventIds = resource.Options.GetIntList("excludedStackEventIds"),
        ProcessIds = resource.Options.GetIntList("processIds"),
        ProcessNames = resource.Options.GetStringList("processNames"),
        EnableInContainers = resource.Options.GetBool("enableInContainers"),
        EnableSourceContainerTracking = resource.Options.GetBool("enableSourceContainerTracking")
    };
}
