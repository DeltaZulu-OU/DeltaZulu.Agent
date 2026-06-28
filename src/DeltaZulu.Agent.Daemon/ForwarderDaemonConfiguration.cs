using DeltaZulu.Pipeline.Outputs.Relp;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DeltaZulu.Agent.Daemon;

public sealed record ForwarderDaemonConfiguration
{
    public string Id { get; init; } = "default-agent-daemon";
    public List<ForwarderDaemonSourceConfiguration> Sources { get; init; } = [];
    public RelpBufferConfiguration Buffer { get; init; } = new();
    public RelpTransportConfiguration Relp { get; init; } = new();
    public ForwarderDaemonDiagnosticsConfiguration Diagnostics { get; init; } = new();
    public ForwarderDaemonOutputConfiguration Output { get; init; } = new();
}

public sealed record ForwarderDaemonSourceConfiguration
{
    public string Id { get; init; } = string.Empty;
    public string Input { get; init; } = string.Empty;
    public string? Target { get; init; }
    public string? Profile { get; init; }
    public string? Address { get; init; }
    public int? Port { get; init; }
    public bool? UseTls { get; init; }
    public string? ServerCertificatePath { get; init; }
    public string? ServerCertificatePassword { get; init; }
}

public sealed record ForwarderDaemonDiagnosticsConfiguration
{
    public double? IntervalSeconds { get; init; }
    public string? File { get; init; }
}

public sealed record ForwarderDaemonOutputConfiguration
{
    public string Mode { get; init; } = "relp";
    public string? File { get; init; }
}

public sealed class YamlForwarderDaemonConfigurationLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

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

    public static void Validate(ForwarderDaemonConfiguration configuration, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var prefix = string.IsNullOrWhiteSpace(path) ? "Agent daemon configuration" : $"Agent daemon configuration '{path}'";
        YamlRelpOutputConfigurationLoader.Validate(new RelpOutputConfiguration
        {
            Id = configuration.Id,
            Buffer = configuration.Buffer,
            Relp = configuration.Relp
        }, path);

        if (configuration.Sources.Count == 0)
        {
            throw new InvalidDataException($"{prefix} must define at least one sources entry.");
        }

        var outputMode = configuration.Output.Mode.ToLowerInvariant();
        if (outputMode is not ("relp" or "console" or "file"))
        {
            throw new InvalidDataException($"{prefix} output.mode must be one of: relp, console, file.");
        }

        if (outputMode == "file" && string.IsNullOrWhiteSpace(configuration.Output.File))
        {
            throw new InvalidDataException($"{prefix} output.file is required when output.mode is file.");
        }

        var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < configuration.Sources.Count; index++)
        {
            var source = configuration.Sources[index];
            if (string.IsNullOrWhiteSpace(source.Id))
            {
                throw new InvalidDataException($"{prefix} sources[{index}].id is required.");
            }

            if (!sourceIds.Add(source.Id))
            {
                throw new InvalidDataException($"{prefix} contains duplicate source id '{source.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(source.Input))
            {
                throw new InvalidDataException($"{prefix} sources[{index}].input is required.");
            }
        }

        if (configuration.Diagnostics.IntervalSeconds is <= 0)
        {
            throw new InvalidDataException($"{prefix} diagnostics.intervalSeconds must be greater than 0 when set.");
        }
    }
}
