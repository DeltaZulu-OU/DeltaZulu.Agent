using System.Security.Cryptography.X509Certificates;

namespace DeltaZulu.Agent.Tunnel;

/// <summary>
/// Minimal RELP mTLS tunnel settings consumed by the agent's RELP transport adapter.
/// </summary>
public sealed record RelpMtlsTunnelOptions
{
    public bool UseTls { get; init; } = true;
    public IReadOnlyList<TunnelEndpoint> Endpoints { get; init; } = [];
    public ITunnelCertificateProvider? CertificateProvider { get; init; }

    public async ValueTask<X509CertificateCollection?> GetClientCertificatesAsync(CancellationToken cancellationToken = default)
    {
        if (!UseTls || CertificateProvider is null)
        {
            return null;
        }

        var lease = await CertificateProvider.GetCurrentCertificateAsync(cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            return null;
        }

        return [lease.Certificate];
    }
}
