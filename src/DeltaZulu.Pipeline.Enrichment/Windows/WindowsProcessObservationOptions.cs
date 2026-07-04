namespace DeltaZulu.Pipeline.Enrichment.Windows;

public sealed record WindowsProcessObservationOptions
{
    public TimeSpan CacheTtl { get; init; } = TimeSpan.FromHours(1);
    public IReadOnlyList<ProcessIdentityFieldMapping> ProcessIdFields { get; init; } = ProcessIdentityFieldMapping.Defaults;
}

public sealed record ProcessIdentityFieldMapping(string ProcessIdField, string Role, string? ImageField = null, string? CommandLineField = null)
{
    public string ResolvedImageField => $"{Role}Image_resolved";
    public string ResolvedCommandLineField => $"{Role}CommandLine_resolved";
    public string ResolutionSourceField => $"{Role}ProcessResolutionSource";
    public string ResolutionStatusField => $"{Role}ProcessResolutionStatus";
    public string ResolutionConfidenceField => $"{Role}ProcessResolutionConfidence";
    public string ResolutionAgeMsField => $"{Role}ProcessResolutionAgeMs";

    public static IReadOnlyList<ProcessIdentityFieldMapping> Defaults { get; } =
    [
        new("ProcessId", "Process", "ProcessName"),
        new("NewProcessId", "NewProcess", "NewProcessName", "CommandLine"),
        new("CreatorProcessId", "CreatorProcess", "ParentProcessName")
    ];
}