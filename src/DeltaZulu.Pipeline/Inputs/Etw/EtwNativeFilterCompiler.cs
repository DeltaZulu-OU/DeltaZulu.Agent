using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Pipeline.Inputs.Etw;

public static class EtwNativeFilterCompiler
{
    private static readonly EtwResourceOptionsAdapter OptionsAdapter = new();

    public static NativeEtwIdentityFilter? Compile(ResourceProfile profile) => Compile(profile.Resource);

    public static NativeEtwIdentityFilter? Compile(ResourceDescriptor resource)
    {
        if (resource.Family.Length > 0 && !resource.Family.Equals("etw", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var options = OptionsAdapter.Adapt(resource);

        var hasProviderName = !string.IsNullOrWhiteSpace(resource.Provider);
        var hasProviderGuid = resource.ProviderGuid.HasValue;
        var hasAllowedEventIds = options.EventIds.Count > 0;
        var hasExcludedEventIds = options.ExcludedEventIds.Count > 0;
        var hasOpcodes = options.Opcodes.Count > 0;
        var hasVersions = options.Versions.Count > 0;

        if (!hasProviderName && !hasProviderGuid && !hasAllowedEventIds && !hasExcludedEventIds && !hasOpcodes && !hasVersions)
        {
            return null;
        }

        return new NativeEtwIdentityFilter {
            ProviderName = hasProviderName ? resource.Provider : null,
            ProviderGuid = resource.ProviderGuid,
            EventIds = options.EventIds.ToHashSet(),
            ExcludedEventIds = options.ExcludedEventIds.ToHashSet(),
            Opcodes = options.Opcodes.ToHashSet(),
            Versions = options.Versions.ToHashSet()
        };
    }
}
