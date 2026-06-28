using DeltaZulu.Pipeline.Core.Profiles;

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
