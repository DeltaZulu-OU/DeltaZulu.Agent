namespace DeltaZulu.Agent.Tunnel;

/// <summary>
/// Network endpoint used by an mTLS-backed tunnel.
/// </summary>
public sealed record TunnelEndpoint
{
    public required string Host { get; init; }
    public required int Port { get; init; }

    public override string ToString() => $"{Host}:{Port}";
}
