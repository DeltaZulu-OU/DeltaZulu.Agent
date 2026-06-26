using System.Security.Cryptography.X509Certificates;

namespace DeltaZulu.Agent.Forwarder;

public sealed record RelpForwarderOptions
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public bool UseTls { get; init; }
    public X509CertificateCollection? ClientCertificates { get; init; }
}
