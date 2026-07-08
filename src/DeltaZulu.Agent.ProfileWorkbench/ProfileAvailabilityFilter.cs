using DeltaZulu.Agent.Filter.Prefilter;
using DeltaZulu.Pipeline.Core.Profiles;

#if WINDOWS
using DeltaZulu.Pipeline.Inputs.Windows;
#endif

namespace DeltaZulu.Agent.ProfileWorkbench;

/// <summary>
/// Applies the same high-level runnability gates that the daemon uses before binding profiles,
/// but keeps the profile workbench catalog quiet. Disabled, platform-mismatched, condition-failed,
/// and unavailable resources are omitted from the list instead of being shown as selectable rows.
/// </summary>
public sealed class ProfileAvailabilityFilter
{
    private readonly ResourceProfilePrefilter _prefilter;

    public ProfileAvailabilityFilter()
        : this(new ResourceProfilePrefilter(DefaultConditionEvaluators.ForCurrentPlatform()))
    {
    }

    public ProfileAvailabilityFilter(ResourceProfilePrefilter prefilter)
    {
        _prefilter = prefilter;
    }

    public bool ShouldList(ResourceProfile profile)
    {
        if (!profile.Enabled)
        {
            return false;
        }

        if (!IsProfileForCurrentPlatform(profile))
        {
            return false;
        }

        if (!IsConditionSatisfied(profile))
        {
            return false;
        }

        if (!IsResourceAvailable(profile))
        {
            return false;
        }

        return true;
    }

    private bool IsConditionSatisfied(ResourceProfile profile)
    {
        try
        {
            return _prefilter.IsSatisfied(profile, out _);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return false;
        }
    }

    private static bool IsProfileForCurrentPlatform(ResourceProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Resource.Platform))
        {
            return true;
        }

        if (profile.Resource.Platform.Equals("portable", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            return profile.Resource.Platform.Equals("windows", StringComparison.OrdinalIgnoreCase);
        }

        if (OperatingSystem.IsLinux())
        {
            return profile.Resource.Platform.Equals("linux", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsResourceAvailable(ResourceProfile profile)
    {
#if WINDOWS
        var validationResult = profile.Resource.Family.ToLowerInvariant() switch
        {
            "eventlog" => WindowsResourceValidator.ValidateEventLog(profile),
            "windows.eventlog" => WindowsResourceValidator.ValidateEventLog(profile),
            "etw" => WindowsResourceValidator.ValidateEtw(profile),
            _ => WindowsResourceValidationResult.Valid
        };

        return validationResult.IsValid;
#else
        return true;
#endif
    }
}
