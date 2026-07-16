namespace DeltaZulu.Pipeline.Tunnel;

/// <summary>
/// Supplies a client identity certificate used by mTLS tunnels.
/// Hosts can implement this with file loading, enrollment, download, renewal, or rotation automation.
/// </summary>
public interface ITunnelCertificateProvider
{
    ValueTask<TunnelCertificateLease?> GetCurrentCertificateAsync(CancellationToken cancellationToken = default);
}
