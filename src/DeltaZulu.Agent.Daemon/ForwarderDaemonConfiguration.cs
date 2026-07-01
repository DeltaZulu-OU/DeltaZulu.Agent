using DeltaZulu.Pipeline.Outputs.Relp;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DeltaZulu.Agent.Daemon;

public sealed record ForwarderDaemonConfiguration
{
    public string Id { get; init; } = "default-agent-daemon";
    public string ProfilesPath { get; init; } = "profiles";
    public ForwarderDaemonPipelineConfiguration Pipeline { get; init; } = new();
    public RelpBufferConfiguration Buffer { get; init; } = new();
    public RelpTransportConfiguration Relp { get; init; } = new();
    public ForwarderDaemonRelpInputConfiguration RelpInput { get; init; } = new();
    public ForwarderDaemonTunnelConfiguration Tunnel { get; init; } = new();
    public ForwarderDaemonDiagnosticsConfiguration Diagnostics { get; init; } = new();
    public ForwarderDaemonResourceQuotaConfiguration ResourceQuotas { get; init; } = new();
}

public sealed record ForwarderDaemonPipelineConfiguration
{
    public ForwarderDaemonPipelineInputConfiguration Input { get; init; } = new();
    public ForwarderDaemonPipelineFilterConfiguration Filter { get; init; } = new();
    public ForwarderDaemonPipelineOutputConfiguration Output { get; init; } = new();
}

public sealed record ForwarderDaemonPipelineInputConfiguration
{
    public string Mode { get; init; } = "profiles";
}

public sealed record ForwarderDaemonRelpInputConfiguration
{
    public string Address { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 6514;
    public bool UseTls { get; init; }
    public string? ServerCertificatePath { get; init; }
    public string? ServerCertificatePassword { get; init; }
}

public sealed record ForwarderDaemonPipelineFilterConfiguration
{
    public string Mode { get; init; } = "profiles";
}

public sealed record ForwarderDaemonPipelineOutputConfiguration
{
    public string Mode { get; init; } = "forward";
    public string Encoding { get; init; } = "messagepack";
    public string Transport { get; init; } = "relp";
    public string? File { get; init; }
    public bool PrettyPrint { get; init; }
}

public sealed record ForwarderDaemonTunnelConfiguration
{
    public bool Enabled { get; init; }
    public ForwarderDaemonTunnelListenConfiguration Listen { get; init; } = new();
}

public sealed record ForwarderDaemonTunnelListenConfiguration
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 2514;
}

public sealed record ForwarderDaemonDiagnosticsConfiguration
{
    public double? IntervalSeconds { get; init; }
    public string? File { get; init; }
    public bool PrettyPrint { get; init; }
}

public sealed record ForwarderDaemonResourceQuotaConfiguration
{
    public int? CpuPercent { get; init; }
}

public sealed class YamlForwarderDaemonConfigurationLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static void Validate(ForwarderDaemonConfiguration configuration, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var prefix = string.IsNullOrWhiteSpace(path) ? "Agent daemon configuration" : $"Agent daemon configuration '{path}'";
        var inputMode = configuration.Pipeline.Input.Mode.ToLowerInvariant();
        if (inputMode == "profiles")
        {
            YamlRelpOutputConfigurationLoader.Validate(new RelpOutputConfiguration {
                Id = configuration.Id,
                Buffer = configuration.Buffer,
                Relp = configuration.Relp
            }, path);
        }

        if (string.IsNullOrWhiteSpace(configuration.ProfilesPath))
        {
            throw new InvalidDataException($"{prefix} profilesPath is required.");
        }

        if (inputMode is not ("profiles" or "relp"))
        {
            throw new InvalidDataException($"{prefix} pipeline.input.mode must be one of: profiles, relp.");
        }

        var filterMode = configuration.Pipeline.Filter.Mode.ToLowerInvariant();
        if (filterMode is not ("profiles" or "passthrough"))
        {
            throw new InvalidDataException($"{prefix} pipeline.filter.mode must be one of: profiles, passthrough.");
        }

        if (inputMode == "profiles" && filterMode != "profiles")
        {
            throw new InvalidDataException($"{prefix} pipeline.filter.mode must be 'profiles' when pipeline.input.mode is profiles.");
        }

        if (inputMode == "relp" && filterMode != "passthrough")
        {
            throw new InvalidDataException($"{prefix} pipeline.filter.mode must be 'passthrough' when pipeline.input.mode is relp.");
        }

        var outputMode = configuration.Pipeline.Output.Mode.ToLowerInvariant();
        if (outputMode is not ("forward" or "console" or "file"))
        {
            throw new InvalidDataException($"{prefix} pipeline.output.mode must be one of: forward, console, file.");
        }

        if (inputMode == "relp" && outputMode == "forward")
        {
            throw new InvalidDataException($"{prefix} pipeline.output.mode cannot be forward when pipeline.input.mode is relp.");
        }

        if (inputMode == "relp")
        {
            if (configuration.RelpInput.Port is < 1 or > 65535)
            {
                throw new InvalidDataException($"{prefix} relpInput.port must be between 1 and 65535.");
            }

            if (string.IsNullOrWhiteSpace(configuration.RelpInput.Address))
            {
                throw new InvalidDataException($"{prefix} relpInput.address is required.");
            }

            if (configuration.RelpInput.UseTls && string.IsNullOrWhiteSpace(configuration.RelpInput.ServerCertificatePath))
            {
                throw new InvalidDataException($"{prefix} relpInput.serverCertificatePath is required when relpInput.useTls is true.");
            }
        }

        if (outputMode == "forward")
        {
            if (!configuration.Pipeline.Output.Encoding.Equals("messagepack", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{prefix} pipeline.output.encoding must be 'messagepack' when pipeline.output.mode is forward.");
            }

            if (!configuration.Pipeline.Output.Transport.Equals("relp", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{prefix} pipeline.output.transport must be 'relp' when pipeline.output.mode is forward.");
            }
        }

        if (outputMode == "file" && string.IsNullOrWhiteSpace(configuration.Pipeline.Output.File))
        {
            throw new InvalidDataException($"{prefix} pipeline.output.file is required when pipeline.output.mode is file.");
        }

        if (configuration.Tunnel.Enabled)
        {
            if (outputMode != "forward")
            {
                throw new InvalidDataException($"{prefix} tunnel.enabled requires pipeline.output.mode to be forward.");
            }

            if (string.IsNullOrWhiteSpace(configuration.Tunnel.Listen.Host))
            {
                throw new InvalidDataException($"{prefix} tunnel.listen.host is required when tunnel.enabled is true.");
            }

            if (configuration.Tunnel.Listen.Port is < 1 or > 65535)
            {
                throw new InvalidDataException($"{prefix} tunnel.listen.port must be between 1 and 65535.");
            }
        }

        if (configuration.Diagnostics.IntervalSeconds is <= 0)
        {
            throw new InvalidDataException($"{prefix} diagnostics.intervalSeconds must be greater than 0 when set.");
        }

        if (configuration.ResourceQuotas.CpuPercent is < 1 or > 100)
        {
            throw new InvalidDataException($"{prefix} resourceQuotas.cpuPercent must be between 1 and 100 when set.");
        }
    }

    public ForwarderDaemonConfiguration LoadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Agent daemon configuration path is required.", nameof(path));
        }

        using var reader = System.IO.File.OpenText(path);
        var configuration = _deserializer.Deserialize<ForwarderDaemonConfiguration>(reader)
            ?? throw new InvalidDataException($"Agent daemon configuration file '{path}' did not contain a configuration.");
        Validate(configuration, path);
        return configuration;
    }
}