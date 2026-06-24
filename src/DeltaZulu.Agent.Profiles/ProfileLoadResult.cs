namespace DeltaZulu.Agent.Profiles;

public sealed record ProfileLoadResult(
    IReadOnlyList<ResourceProfile> Profiles,
    IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;
}