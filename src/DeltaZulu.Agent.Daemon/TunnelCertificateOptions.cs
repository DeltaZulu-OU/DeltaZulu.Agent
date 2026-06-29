namespace DeltaZulu.Agent.Daemon;

/// <summary>
/// File-based client certificate configuration for outbound mTLS tunnels.
/// </summary>
public sealed record TunnelCertificateOptions
{
    public bool Enabled { get; init; } = true;
    public string? CertificatePath { get; init; }
    public string? CertificatePassword { get; init; }
    public string? PrivateKeyPath { get; init; }
    public int RenewalWindowDays { get; init; } = 30;
}