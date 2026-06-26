using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DeltaZulu.Agent.Forwarder;

public sealed record ForwarderConfiguration
{
    public string Id { get; init; } = "default-forwarder";
    public ForwarderBufferConfiguration Buffer { get; init; } = new();
    public ForwarderRelpConfiguration Relp { get; init; } = new();
}

public sealed record ForwarderBufferConfiguration
{
    public string Path { get; init; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "deltazulu-agent-forwarder");
    public int MaxChunkRecords { get; init; } = 100;
    public double MaxChunkAgeSeconds { get; init; } = 1;
}

public sealed record ForwarderRelpConfiguration
{
    public bool UseTls { get; init; }
    public ForwarderTlsConfiguration Tls { get; init; } = new();
    public List<RelpEndpoint> Endpoints { get; init; } = [new RelpEndpoint { Host = "127.0.0.1", Port = 6514 }];
}

public sealed record ForwarderTlsConfiguration
{
    public RelpCertificateValidationMode CertificateValidation { get; init; } = RelpCertificateValidationMode.SystemTrust;
    public List<string> AllowedServerCertificateThumbprints { get; init; } = [];
    public int CertificateExpiryWarningDays { get; init; } = 30;
    public string? ClientCertificatePath { get; init; }
    public string? ClientCertificatePassword { get; init; }
}

public sealed class YamlForwarderConfigurationLoader
{
    private readonly IDeserializer _deserializer;

    public YamlForwarderConfigurationLoader()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public ForwarderConfiguration LoadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Forwarder configuration path is required.", nameof(path));
        }

        using var reader = File.OpenText(path);
        var configuration = _deserializer.Deserialize<ForwarderConfiguration>(reader)
            ?? throw new InvalidDataException($"Forwarder configuration file '{path}' did not contain a configuration.");
        Validate(configuration, path);
        return configuration;
    }

    public static void Validate(ForwarderConfiguration configuration, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var prefix = string.IsNullOrWhiteSpace(path) ? "Forwarder configuration" : $"Forwarder configuration '{path}'";

        if (configuration.Buffer.MaxChunkRecords < 1)
        {
            throw new InvalidDataException($"{prefix} must set buffer.maxChunkRecords to at least 1.");
        }

        if (configuration.Buffer.MaxChunkAgeSeconds <= 0)
        {
            throw new InvalidDataException($"{prefix} must set buffer.maxChunkAgeSeconds greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(configuration.Buffer.Path))
        {
            throw new InvalidDataException($"{prefix} must set buffer.path.");
        }

        if (!configuration.Relp.UseTls && configuration.Relp.Tls.CertificateValidation != RelpCertificateValidationMode.SystemTrust)
        {
            throw new InvalidDataException($"{prefix} must enable relp.useTls before setting relp.tls.certificateValidation.");
        }

        if (configuration.Relp.Tls.CertificateValidation == RelpCertificateValidationMode.Thumbprint
            && configuration.Relp.Tls.AllowedServerCertificateThumbprints.Count == 0)
        {
            throw new InvalidDataException($"{prefix} must set relp.tls.allowedServerCertificateThumbprints when relp.tls.certificateValidation is Thumbprint.");
        }

        if (configuration.Relp.Tls.CertificateExpiryWarningDays < 0)
        {
            throw new InvalidDataException($"{prefix} must set relp.tls.certificateExpiryWarningDays to zero or greater.");
        }

        if (!string.IsNullOrWhiteSpace(configuration.Relp.Tls.ClientCertificatePath)
            && !File.Exists(configuration.Relp.Tls.ClientCertificatePath))
        {
            throw new InvalidDataException($"{prefix} relp.tls.clientCertificatePath does not exist: {configuration.Relp.Tls.ClientCertificatePath}");
        }

        if (configuration.Relp.Endpoints.Count == 0)
        {
            throw new InvalidDataException($"{prefix} must define at least one relp.endpoints entry.");
        }

        for (var index = 0; index < configuration.Relp.Endpoints.Count; index++)
        {
            var endpoint = configuration.Relp.Endpoints[index];
            if (string.IsNullOrWhiteSpace(endpoint.Host))
            {
                throw new InvalidDataException($"{prefix} relp.endpoints[{index}].host is required.");
            }

            if (endpoint.Port is < 1 or > 65535)
            {
                throw new InvalidDataException($"{prefix} relp.endpoints[{index}].port must be between 1 and 65535.");
            }
        }
    }
}
