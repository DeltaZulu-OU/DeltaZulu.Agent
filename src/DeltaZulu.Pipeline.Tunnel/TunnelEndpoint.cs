namespace DeltaZulu.Pipeline.Tunnel;

/// <summary>
/// Network endpoint used by a TCP tunnel.
/// </summary>
public sealed record TunnelEndpoint
{
    public required string Host { get; init; }
    public required int Port { get; init; }

    public override string ToString() => $"{Host}:{Port}";
}