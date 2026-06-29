using System.ComponentModel;
using System.Security.Principal;
using DeltaZulu.Pipeline.Core.Profiles;
using Microsoft.Diagnostics.Tracing.Session;

namespace DeltaZulu.Pipeline.Inputs.Windows;

/// <summary>
/// Validates Windows-specific resource availability and requirements.
/// </summary>
public static class WindowsResourceValidator
{
    /// <summary>
    /// Validates Event Log availability for a given profile.
    /// </summary>
    public static WindowsResourceValidationResult ValidateEventLog(
        ResourceProfile profile,
        string? inputArgument = null)
    {
        var logName = inputArgument ?? profile.Resource.Channel;

        if (string.IsNullOrWhiteSpace(logName))
        {
            var message = $"profile '{profile.Id}' requires resource.channel or an eventlog <logname> argument.";
            return profile.Mandatory
                ? WindowsResourceValidationResult.Error(message)
                : WindowsResourceValidationResult.Warning($"Skipping optional {message}");
        }

        if (WindowsEventLogInput.TryValidateLogReadable(logName, out _, out var errorMessage))
        {
            return WindowsResourceValidationResult.Valid;
        }

        if (WindowsEventLogInput.IsDisabledChannelError(errorMessage))
        {
            return profile.Mandatory
                ? WindowsResourceValidationResult.Error($"profile '{profile.Id}' requires Event Log '{logName}' but {errorMessage}")
                : WindowsResourceValidationResult.Warning($"Skipping optional profile '{profile.Id}' because {errorMessage}");
        }

        return profile.Mandatory
            ? WindowsResourceValidationResult.Error(errorMessage ?? $"Unable to validate Windows Event Log '{logName}'.")
            : WindowsResourceValidationResult.Warning($"Skipping optional profile '{profile.Id}' because mandatory is false: {errorMessage}");
    }

    /// <summary>
    /// Validates ETW availability for a given profile. Attach-mode profiles require an already-running session;
    /// managed profiles require a session name and provider so the agent can create and enable the session.
    /// </summary>
    public static WindowsResourceValidationResult ValidateEtw(ResourceProfile profile, string? inputArgument = null)
    {
        var sessionName = inputArgument ?? profile.Resource.Session;

        if (string.IsNullOrWhiteSpace(sessionName))
        {
            var message = $"profile '{profile.Id}' requires resource.session or an etw <session> argument.";
            return profile.Mandatory
                ? WindowsResourceValidationResult.Error(message)
                : WindowsResourceValidationResult.Warning($"Skipping optional {message}");
        }

        if (!IsAdministrator())
        {
            var action = IsManagedEtw(profile) ? "create and attach to" : "attach to";
            var message = $"profile '{profile.Id}' requires Administrator privileges to {action} ETW session '{sessionName}'.";
            return profile.Mandatory
                ? WindowsResourceValidationResult.Error(message)
                : WindowsResourceValidationResult.Warning($"Skipping optional profile '{profile.Id}' because {message}");
        }

        if (IsManagedEtw(profile))
        {
            if (string.IsNullOrWhiteSpace(profile.Resource.Provider))
            {
                var message = $"profile '{profile.Id}' requires resource.provider for managed etw.";
                return profile.Mandatory
                    ? WindowsResourceValidationResult.Error(message)
                    : WindowsResourceValidationResult.Warning($"Skipping optional {message}");
            }

            return WindowsResourceValidationResult.Valid;
        }

        try
        {
            if (TraceEventSession.GetActiveSessionNames().Any(name => name.Equals(sessionName, StringComparison.OrdinalIgnoreCase)))
            {
                return WindowsResourceValidationResult.Valid;
            }

            var message = $"ETW session '{sessionName}' is not active on this host. DeltaZulu ETW input is attach-only; create and enable the session before starting the profile, or set resource.mode to 'managed' with resource.provider.";
            return profile.Mandatory
                ? WindowsResourceValidationResult.Error($"profile '{profile.Id}' requires {message}")
                : WindowsResourceValidationResult.Warning($"Skipping optional profile '{profile.Id}' because {message}");
        }
        catch (Win32Exception ex)
        {
            var message = $"Unable to enumerate active ETW sessions while validating profile '{profile.Id}': {ex.Message}";
            return profile.Mandatory
                ? WindowsResourceValidationResult.Error(message)
                : WindowsResourceValidationResult.Warning($"Skipping optional profile '{profile.Id}' because {message}");
        }
    }

    private static bool IsManagedEtw(ResourceProfile profile) =>
        profile.Resource.Mode.Equals("managed", StringComparison.OrdinalIgnoreCase);

    private static bool IsAdministrator()
    {
        var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

}

/// <summary>
/// Result of Windows resource validation.
/// </summary>
public sealed record WindowsResourceValidationResult(bool IsValid, string? ErrorMessage = null, string? WarningMessage = null)
{
    public static WindowsResourceValidationResult Valid { get; } = new(true);

    public static WindowsResourceValidationResult Error(string message) => new(false, ErrorMessage: message);

    public static WindowsResourceValidationResult Warning(string message) => new(false, WarningMessage: message);
}
