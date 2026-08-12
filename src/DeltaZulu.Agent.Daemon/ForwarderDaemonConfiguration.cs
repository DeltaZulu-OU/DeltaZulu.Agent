using DeltaZulu.Pipeline.Outputs.Forwarder;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DeltaZulu.Agent.Daemon;

public sealed record ForwarderDaemonConfiguration
{
    public string Id { get; init; } = "default-agent-daemon";
    public string ProfilesPath { get; init; } = "profiles";
    public ForwarderDaemonPipelineConfiguration Pipeline { get; init; } = new();
    public ForwarderBufferConfiguration Buffer { get; init; } = new();
    [YamlMember(Alias = "forwarder")]
    public ForwarderTransportConfiguration Transport { get; init; } = new();
    public ForwarderDaemonInputConfiguration InputConf { get; init; } = new();
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

public sealed record ForwarderDaemonInputConfiguration
{
    public string Address { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 2514;
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
    public string Encoding { get; init; } = "forward";
    public string Transport { get; init; } = "forwarder";
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
    public string? SqliteFile { get; init; }
    public double? SqliteIntervalSeconds { get; init; }
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
            YamlForwarderOutputConfigurationLoader.Validate(new ForwarderOutputConfiguration {
                Id = configuration.Id,
                Buffer = configuration.Buffer,
                Transport = configuration.Transport
            }, path);
        }

        if (string.IsNullOrWhiteSpace(configuration.ProfilesPath))
        {
            throw new InvalidDataException($"{prefix} profilesPath is required.");
        }

        if (inputMode is not ("profiles" or "forwarder"))
        {
            throw new InvalidDataException($"{prefix} pipeline.input.mode must be one of: profiles, forwarder.");
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

        if (inputMode == "forwarder" && filterMode != "passthrough")
        {
            throw new InvalidDataException($"{prefix} pipeline.filter.mode must be 'passthrough' when pipeline.input.mode is forwarder.");
        }

        var outputMode = configuration.Pipeline.Output.Mode.ToLowerInvariant();
        if (outputMode is not ("forward" or "console" or "file"))
        {
            throw new InvalidDataException($"{prefix} pipeline.output.mode must be one of: forward, console, file.");
        }

        if (inputMode == "forwarder" && outputMode == "forward")
        {
            throw new InvalidDataException($"{prefix} pipeline.output.mode cannot be forward when pipeline.input.mode is forwarder.");
        }

        if (inputMode == "forwarder")
        {
            if (configuration.InputConf.Port is < 1 or > 65535)
            {
                throw new InvalidDataException($"{prefix} forwarderInput.port must be between 1 and 65535.");
            }

            if (string.IsNullOrWhiteSpace(configuration.InputConf.Address))
            {
                throw new InvalidDataException($"{prefix} forwarderInput.address is required.");
            }

            if (configuration.InputConf.UseTls && string.IsNullOrWhiteSpace(configuration.InputConf.ServerCertificatePath))
            {
                throw new InvalidDataException($"{prefix} forwarderInput.serverCertificatePath is required when forwarderInput.useTls is true.");
            }
        }

        if (outputMode == "forward")
        {
            if (!configuration.Pipeline.Output.Encoding.Equals("forward", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{prefix} pipeline.output.encoding must be 'forward' when pipeline.output.mode is forward.");
            }

            if (!configuration.Pipeline.Output.Transport.Equals("forwarder", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{prefix} pipeline.output.transport must be 'forwarder' when pipeline.output.mode is forward.");
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

        if (configuration.Diagnostics.SqliteIntervalSeconds is <= 0)
        {
            throw new InvalidDataException($"{prefix} diagnostics.sqliteIntervalSeconds must be greater than 0 when set.");
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

        using var reader = File.OpenText(path);
        var configuration = _deserializer.Deserialize<ForwarderDaemonConfiguration>(reader)
            ?? throw new InvalidDataException($"Agent daemon configuration file '{path}' did not contain a configuration.");
        Validate(configuration, path);
        return configuration;
    }
}