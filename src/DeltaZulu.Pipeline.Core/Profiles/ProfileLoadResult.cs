namespace DeltaZulu.Pipeline.Core.Profiles;

public sealed record ProfileLoadResult(
    IReadOnlyList<ResourceProfile> Profiles,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool Success => Errors.Count == 0;
}