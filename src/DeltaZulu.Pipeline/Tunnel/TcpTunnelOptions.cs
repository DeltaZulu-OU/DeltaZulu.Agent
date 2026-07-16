using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace DeltaZulu.Pipeline.Tunnel;

/// <summary>
/// TCP tunnel settings, including local listen address, remote endpoints, and optional TLS/mTLS material.
/// </summary>
public sealed record TcpTunnelOptions
{
    public TunnelEndpoint ListenEndpoint { get; init; } = new() { Host = "127.0.0.1", Port = 2514 };
    public bool UseTls { get; init; } = true;
    public IReadOnlyList<TunnelEndpoint> Endpoints { get; init; } = [];
    public ITunnelCertificateProvider? CertificateProvider { get; init; }
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; }

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
