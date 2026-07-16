namespace DeltaZulu.Pipeline.Core.Checkpoints;

/// <summary>
/// Cross-input persistence for a source's resume position ("bookmark"). The token is an opaque
/// string whose meaning is defined by each input (for Windows Event Log it is the last delivered
/// EventRecordID; for a file tail it could be a byte offset), so a single store serves every input
/// family rather than each input inventing its own persistence.
/// </summary>
/// <remarks>
/// Advancement boundary: a checkpoint should be saved only once the corresponding events have
/// crossed the agent's durability boundary (durable-buffer enqueue), per the project's delivery
/// model — not after mere in-memory handoff and not after network ACK. In the current synchronous
/// pipeline, a source's <c>OnNext</c> blocks until the record has been written to the durable
/// buffer, so saving after a batch's handoff returns satisfies that boundary.
/// </remarks>
public interface ISourceCheckpointStore
{
    /// <summary>Persists the token for a source key, replacing any previous value.</summary>
    void Save(string sourceKey, string token);

    /// <summary>Attempts to load the persisted token for a source key. Returns false when absent or unreadable.</summary>
    bool TryLoad(string sourceKey, out string token);
}
