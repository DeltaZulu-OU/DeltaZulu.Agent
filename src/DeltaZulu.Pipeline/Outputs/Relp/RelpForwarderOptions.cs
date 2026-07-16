using System.Security.Cryptography.X509Certificates;

namespace DeltaZulu.Pipeline.Outputs.Relp;

public sealed record RelpEndpoint
{
    public required string Host { get; init; }
    public required int Port { get; init; }

    public override string ToString() => $"{Host}:{Port}";
}

public enum RelpCertificateValidationMode
{
    SystemTrust,
    Thumbprint,
    Disabled
}

public sealed record RelpForwarderOptions
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public IReadOnlyList<RelpEndpoint>? Endpoints { get; init; }
    public bool UseTls { get; init; }
    public X509CertificateCollection? ClientCertificates { get; init; }
    public RelpCertificateValidationMode CertificateValidation { get; init; } = RelpCertificateValidationMode.SystemTrust;
    public IReadOnlyList<string> AllowedServerCertificateThumbprints { get; init; } = [];
    public int CertificateExpiryWarningDays { get; init; } = 30;

    public IReadOnlyList<RelpEndpoint> GetConfiguredEndpoints()
    {
        if (Endpoints is { Count: > 0 })
        {
            return Endpoints;
        }

        return [new RelpEndpoint { Host = Host, Port = Port }];
    }
}
