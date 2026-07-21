using System.Security.Cryptography.X509Certificates;
using DeltaZulu.DurableBuffer.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DeltaZulu.Pipeline.Outputs.Forwarder;

public sealed record ForwarderOutputConfiguration
{
    public string Id { get; init; } = "default-forwarder";
    public ForwarderBufferConfiguration Buffer { get; init; } = new();
    [YamlMember(Alias = "forwarder")]
    public ForwarderTransportConfiguration Transport { get; init; } = new();
}

public sealed record ForwarderBufferConfiguration
{
    public string Path { get; init; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "deltazulu-agent-forwarder");

    public long MaxDiskBytes { get; init; } = 512L * 1024 * 1024;
    public long MaxMemoryBytes { get; init; } = 32L * 1024 * 1024;
    public long MaxDeadLetterBytes { get; init; } = 64L * 1024 * 1024;
    public long MaxQuarantineBytes { get; init; } = 64L * 1024 * 1024;

    public int MaxChunkRecords { get; init; } = 100;
    public long MaxChunkBytes { get; init; } = 4L * 1024 * 1024;
    public double MaxChunkAgeSeconds { get; init; } = 1;

    public BufferFullPolicy FullPolicy { get; init; } = BufferFullPolicy.Block;
    public ForwarderRetryExhaustedPolicy RetryExhaustedPolicy { get; init; } = ForwarderRetryExhaustedPolicy.DeadLetter;

    public int MaxRetryAttempts { get; init; } = 10;
    public double RetryBaseDelaySeconds { get; init; } = 1;
    public double RetryMaxDelaySeconds { get; init; } = 300;

    public DurableBufferOptions ToBufferOptions() => new() {
        StoragePath = Path,
        MaxDiskBytes = MaxDiskBytes,
        MaxMemoryBytes = MaxMemoryBytes,
        MaxDeadLetterBytes = MaxDeadLetterBytes,
        MaxQuarantineBytes = MaxQuarantineBytes,
        MaxChunkRecords = MaxChunkRecords,
        MaxChunkBytes = MaxChunkBytes,
        MaxChunkAge = TimeSpan.FromSeconds(MaxChunkAgeSeconds),
        FullPolicy = FullPolicy
    };

    public ForwarderRetryConfiguration ToRetryConfiguration() => new() {
        MaxAttempts = MaxRetryAttempts,
        BaseDelay = TimeSpan.FromSeconds(RetryBaseDelaySeconds),
        MaxDelay = TimeSpan.FromSeconds(RetryMaxDelaySeconds),
        ExhaustedPolicy = RetryExhaustedPolicy
    };
}

public sealed record ForwarderTransportConfiguration
{
    public bool UseTls { get; init; }
    public ForwarderTlsConfiguration Tls { get; init; } = new();
    public List<ForwarderEndpoint> Endpoints { get; init; } = [new ForwarderEndpoint { Host = "127.0.0.1", Port = 2514 }];

    /// <summary>
    /// Creates the transport options consumed by the FORWARDER client. Callers can provide an
    /// alternate endpoint list or TLS mode when a local tunnel fronts the configured target.
    /// </summary>
    public ForwarderOptions ToForwarderOptions(
        X509CertificateCollection? clientCertificates = null,
        IReadOnlyList<ForwarderEndpoint>? endpoints = null,
        bool? useTls = null)
    {
        var effectiveEndpoints = endpoints ?? Endpoints;
        if (effectiveEndpoints.Count == 0)
        {
            throw new InvalidOperationException("FORWARDER transport requires at least one endpoint.");
        }

        var primaryEndpoint = effectiveEndpoints[0];
        return new ForwarderOptions {
            Host = primaryEndpoint.Host,
            Port = primaryEndpoint.Port,
            Endpoints = effectiveEndpoints,
            UseTls = useTls ?? UseTls,
            ClientCertificates = clientCertificates,
            CertificateValidation = Tls.CertificateValidation,
            AllowedServerCertificateThumbprints = Tls.AllowedServerCertificateThumbprints,
            CertificateExpiryWarningDays = Tls.CertificateExpiryWarningDays
        };
    }
}

public sealed record ForwarderTlsConfiguration
{
    public CertificateValidationMode CertificateValidation { get; init; } = CertificateValidationMode.SystemTrust;
    public List<string> AllowedServerCertificateThumbprints { get; init; } = [];
    public int CertificateExpiryWarningDays { get; init; } = 30;
    public bool ClientCertificateEnabled { get; init; } = true;
    public string? ClientCertificatePath { get; init; }
    public string? ClientCertificatePassword { get; init; }
}

public sealed class YamlForwarderOutputConfigurationLoader
{
    private readonly IDeserializer _deserializer;

    public YamlForwarderOutputConfigurationLoader()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public static void Validate(ForwarderOutputConfiguration configuration, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var prefix = string.IsNullOrWhiteSpace(path) ? "Forwarder configuration" : $"Forwarder configuration '{path}'";

        if (string.IsNullOrWhiteSpace(configuration.Buffer.Path))
        {
            throw new InvalidDataException($"{prefix} must set buffer.path.");
        }

        if (configuration.Buffer.MaxDiskBytes < 1)
        {
            throw new InvalidDataException($"{prefix} must set buffer.maxDiskBytes to at least 1.");
        }

        if (configuration.Buffer.MaxMemoryBytes < 1)
        {
            throw new InvalidDataException($"{prefix} must set buffer.maxMemoryBytes to at least 1.");
        }

        if (configuration.Buffer.MaxDeadLetterBytes < 1)
        {
            throw new InvalidDataException($"{prefix} must set buffer.maxDeadLetterBytes to at least 1.");
        }

        if (configuration.Buffer.MaxQuarantineBytes < 1)
        {
            throw new InvalidDataException($"{prefix} must set buffer.maxQuarantineBytes to at least 1.");
        }

        if (configuration.Buffer.MaxChunkRecords < 1)
        {
            throw new InvalidDataException($"{prefix} must set buffer.maxChunkRecords to at least 1.");
        }

        if (configuration.Buffer.MaxChunkBytes < 1)
        {
            throw new InvalidDataException($"{prefix} must set buffer.maxChunkBytes to at least 1.");
        }

        if (configuration.Buffer.MaxChunkAgeSeconds <= 0)
        {
            throw new InvalidDataException($"{prefix} must set buffer.maxChunkAgeSeconds greater than 0.");
        }

        if (configuration.Buffer.MaxRetryAttempts < 1)
        {
            throw new InvalidDataException($"{prefix} must set buffer.maxRetryAttempts to at least 1.");
        }

        if (configuration.Buffer.RetryBaseDelaySeconds <= 0)
        {
            throw new InvalidDataException($"{prefix} must set buffer.retryBaseDelaySeconds greater than 0.");
        }

        if (configuration.Buffer.RetryMaxDelaySeconds <= 0)
        {
            throw new InvalidDataException($"{prefix} must set buffer.retryMaxDelaySeconds greater than 0.");
        }

        if (!configuration.Transport.UseTls && configuration.Transport.Tls.CertificateValidation != CertificateValidationMode.SystemTrust)
        {
            throw new InvalidDataException($"{prefix} must enable forwarder.useTls before setting forwarder.tls.certificateValidation.");
        }

        if (configuration.Transport.Tls.CertificateValidation == CertificateValidationMode.Thumbprint
            && configuration.Transport.Tls.AllowedServerCertificateThumbprints.Count == 0)
        {
            throw new InvalidDataException($"{prefix} must set forwarder.tls.allowedServerCertificateThumbprints when forwarder.tls.certificateValidation is Thumbprint.");
        }

        if (configuration.Transport.Tls.CertificateExpiryWarningDays < 0)
        {
            throw new InvalidDataException($"{prefix} must set forwarder.tls.certificateExpiryWarningDays to zero or greater.");
        }

        if (configuration.Transport.Tls.ClientCertificateEnabled
            && !string.IsNullOrWhiteSpace(configuration.Transport.Tls.ClientCertificatePath)
            && !File.Exists(configuration.Transport.Tls.ClientCertificatePath))
        {
            throw new InvalidDataException($"{prefix} forwarder.tls.clientCertificatePath does not exist: {configuration.Transport.Tls.ClientCertificatePath}");
        }

        if (configuration.Transport.Endpoints.Count == 0)
        {
            throw new InvalidDataException($"{prefix} must define at least one forwarder.endpoints entry.");
        }

        for (var index = 0; index < configuration.Transport.Endpoints.Count; index++)
        {
            var endpoint = configuration.Transport.Endpoints[index];
            if (string.IsNullOrWhiteSpace(endpoint.Host))
            {
                throw new InvalidDataException($"{prefix} forwarder.endpoints[{index}].host is required.");
            }

            if (endpoint.Port is < 1 or > 65535)
            {
                throw new InvalidDataException($"{prefix} forwarder.endpoints[{index}].port must be between 1 and 65535.");
            }
        }
    }

    public ForwarderOutputConfiguration LoadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Forwarder configuration path is required.", nameof(path));
        }

        using var reader = File.OpenText(path);
        var configuration = _deserializer.Deserialize<ForwarderOutputConfiguration>(reader)
            ?? throw new InvalidDataException($"Forwarder configuration file '{path}' did not contain a configuration.");
        Validate(configuration, path);
        return configuration;
    }
}
