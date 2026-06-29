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
        YamlRelpOutputConfigurationLoader.Validate(new RelpOutputConfiguration {
            Id = configuration.Id,
            Buffer = configuration.Buffer,
            Relp = configuration.Relp
        }, path);

        if (string.IsNullOrWhiteSpace(configuration.ProfilesPath))
        {
            throw new InvalidDataException($"{prefix} profilesPath is required.");
        }

        if (!configuration.Pipeline.Input.Mode.Equals("profiles", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{prefix} pipeline.input.mode must be 'profiles'.");
        }

        if (!configuration.Pipeline.Filter.Mode.Equals("profiles", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{prefix} pipeline.filter.mode must be 'profiles'.");
        }

        var outputMode = configuration.Pipeline.Output.Mode.ToLowerInvariant();
        if (outputMode is not ("forward" or "console" or "file"))
        {
            throw new InvalidDataException($"{prefix} pipeline.output.mode must be one of: forward, console, file.");
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