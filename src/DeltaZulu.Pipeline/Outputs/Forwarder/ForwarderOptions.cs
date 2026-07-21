using System.Security.Cryptography.X509Certificates;

namespace DeltaZulu.Pipeline.Outputs.Forwarder;

public sealed record ForwarderEndpoint
{
    public required string Host { get; init; }
    public required int Port { get; init; }

    public override string ToString() => $"{Host}:{Port}";
}

public enum CertificateValidationMode
{
    SystemTrust,
    Thumbprint,
    Disabled
}

public sealed record ForwarderOptions
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public IReadOnlyList<ForwarderEndpoint>? Endpoints { get; init; }
    public bool UseTls { get; init; }
    public X509CertificateCollection? ClientCertificates { get; init; }
    public CertificateValidationMode CertificateValidation { get; init; } = CertificateValidationMode.SystemTrust;
    public IReadOnlyList<string> AllowedServerCertificateThumbprints { get; init; } = [];
    public int CertificateExpiryWarningDays { get; init; } = 30;

    public IReadOnlyList<ForwarderEndpoint> GetConfiguredEndpoints()
    {
        if (Endpoints is { Count: > 0 })
        {
            return Endpoints;
        }

        return [new ForwarderEndpoint { Host = Host, Port = Port }];
    }
}
