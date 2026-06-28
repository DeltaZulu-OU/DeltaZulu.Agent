using System.Security.Cryptography.X509Certificates;

namespace DeltaZulu.Agent.Tunnel;

/// <summary>
/// Represents the currently usable agent certificate and renewal metadata.
/// </summary>
public sealed record TunnelCertificateLease(
    X509Certificate2 Certificate,
    DateTimeOffset NotBefore,
    DateTimeOffset ExpiresAt,
    DateTimeOffset RenewAfter)
{
    public bool IsUsable(DateTimeOffset now) => now >= NotBefore && now < ExpiresAt;

    public bool ShouldRenew(DateTimeOffset now) => now >= RenewAfter;
}
