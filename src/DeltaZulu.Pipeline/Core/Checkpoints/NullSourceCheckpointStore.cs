namespace DeltaZulu.Pipeline.Core.Checkpoints;

/// <summary>
/// A no-op checkpoint store used when bookmarking is disabled: nothing is ever persisted and every
/// load misses, so the input falls back to its configured start position on each run.
/// </summary>
public sealed class NullSourceCheckpointStore : ISourceCheckpointStore
{
    public static NullSourceCheckpointStore Instance { get; } = new();

    public void Save(string sourceKey, string token)
    {
    }

    public bool TryLoad(string sourceKey, out string token)
    {
        token = string.Empty;
        return false;
    }
}
