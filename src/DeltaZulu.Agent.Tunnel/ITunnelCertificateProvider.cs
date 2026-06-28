namespace DeltaZulu.Agent.Tunnel;

/// <summary>
/// Supplies the agent identity certificate used by mTLS tunnels.
/// Implementations may load from disk today and later automate enrollment, download, renewal, and rotation.
/// </summary>
public interface ITunnelCertificateProvider
{
    ValueTask<TunnelCertificateLease?> GetCurrentCertificateAsync(CancellationToken cancellationToken = default);
}
