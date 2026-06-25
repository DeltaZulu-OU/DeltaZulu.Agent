namespace DeltaZulu.Buffer.Recovery;

public interface IRecoveryManager
{
    ValueTask<RecoverySummary> RecoverAsync(CancellationToken cancellationToken = default);
}