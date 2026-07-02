using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Pipeline.Inputs.Etw;

public static class EtwNativeFilterCompiler
{
    public static NativeEtwIdentityFilter? Compile(ResourceProfile profile) => Compile(profile.Resource);

    public static NativeEtwIdentityFilter? Compile(ResourceDescriptor resource)
    {
        if (resource.Family.Length > 0 && !resource.Family.Equals("etw", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hasProviderName = !string.IsNullOrWhiteSpace(resource.Provider);
        var hasProviderGuid = resource.ProviderGuid.HasValue;
        var hasAllowedEventIds = resource.EtwEventIds.Count > 0;
        var hasExcludedEventIds = resource.EtwExcludedEventIds.Count > 0;
        var hasOpcodes = resource.EtwOpcodes.Count > 0;
        var hasVersions = resource.EtwVersions.Count > 0;

        if (!hasProviderName && !hasProviderGuid && !hasAllowedEventIds && !hasExcludedEventIds && !hasOpcodes && !hasVersions)
        {
            return null;
        }

        return new NativeEtwIdentityFilter {
            ProviderName = hasProviderName ? resource.Provider : null,
            ProviderGuid = resource.ProviderGuid,
            EventIds = resource.EtwEventIds.ToHashSet(),
            ExcludedEventIds = resource.EtwExcludedEventIds.ToHashSet(),
            Opcodes = resource.EtwOpcodes.ToHashSet(),
            Versions = resource.EtwVersions.ToHashSet()
        };
    }
}
