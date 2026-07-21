using DeltaZulu.Agent.Runtime;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Observability;
using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Pipeline.Inputs.Auditd;
using DeltaZulu.Pipeline.Inputs.Syslog;
using DeltaZulu.Agent.Filter.Kql;
using DeltaZulu.Pipeline.Outputs.Ndjson;
using DeltaZulu.Pipeline.Tunnel;
using DeltaZulu.Agent.Filter.Prefilter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DeltaZulu.Pipeline.Outputs.Forwarder;
using DeltaZulu.Pipeline.Inputs.Forwarder;

#if WINDOWS
using DeltaZulu.Pipeline.Inputs.Windows;
#endif

namespace DeltaZulu.Agent.Daemon;

internal static class Program
{
    private const string DefaultConfigPath = "config/dzagent.yaml";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var configPath = ParseConfigPath(args);
        try
        {
            await ConfigureHostLifetime(Host.CreateDefaultBuilder(args))
                .ConfigureServices(services => services.AddHostedService(provider => new ForwarderDaemonService(
                    configPath,
                    provider.GetRequiredService<ILogger<ForwarderDaemonService>>())))
                .Build()
                .RunAsync();

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: agent daemon failed");
            Console.Error.WriteLine(ex);
            Console.Error.Flush();
            return 1;
        }
    }

    private static IHostBuilder ConfigureHostLifetime(IHostBuilder builder)
    {
        if (OperatingSystem.IsWindows())
        {
            return builder.UseWindowsService(options => options.ServiceName = "DeltaZulu Agent Daemon");
        }

        if (OperatingSystem.IsLinux())
        {
            return builder.UseSystemd();
        }

        return builder;
    }

    private static string ParseConfigPath(string[] args)
    {
        var configPath = DefaultConfigPath;
        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (token is "--config" or "-c")
            {
                if (++index >= args.Length)
                {
                    throw new ArgumentException($"{token} requires a file path.");
                }

                configPath = args[index];
                continue;
            }

            if (!token.StartsWith('-') && configPath == DefaultConfigPath)
            {
                configPath = token;
                continue;
            }

            if (token is not "--console")
            {
                throw new ArgumentException($"unknown daemon option '{token}'. Only --config is supported; this executable is configuration-driven.");
            }
        }

        return configPath;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
Usage:
  dzagentd --config <config.yaml>
  dzagentd <config.yaml>

This executable is service-shaped from the start: it hosts configured DeltaZulu daemon pipelines and has no query, table, JSON export, or schema commands. Run it with config/dzagent.yaml for agent forwarding or config/dzcollector.yaml for local FORWARDER receiver validation.
""");
        Console.Out.Flush();
    }
}

internal sealed class ForwarderDaemonService(string configPath, ILogger<ForwarderDaemonService> logger) : BackgroundService
{
    private static readonly Action<ILogger, string, string, Exception?> StartingAgentDaemon =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1, nameof(StartingAgentDaemon)),
            "Starting DeltaZulu agent daemon {AgentId} from {ConfigPath}.");

    private static readonly Action<ILogger, string, string, int, Exception?> ConfiguredForwarderCollectorInput =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Information,
            new EventId(2, nameof(ConfiguredForwarderCollectorInput)),
            "Configured Forwarder collector input for daemon {AgentId} on {Address}:{Port}.");

    private static readonly Action<ILogger, string, string, string, Exception?> ConfiguredProfile =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information,
            new EventId(3, nameof(ConfiguredProfile)),
            "Configured profile {ProfileId} ({Family}) for daemon {AgentId}.");

    private static readonly Action<ILogger, string, Exception?> ProfileConditionNotSatisfied =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4, nameof(ProfileConditionNotSatisfied)),
            "Profile '{ProfileId}' condition is not satisfied.");

    private readonly List<IDisposable> _disposables = [];
    private readonly ResourceProfilePrefilter _prefilter = new(DefaultConditionEvaluators.ForCurrentPlatform());

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = new YamlForwarderDaemonConfigurationLoader().LoadFile(configPath);
        StartingAgentDaemon(logger, configuration.Id, configPath, null);
        ApplyResourceQuotas(configuration);

        var outputSink = CreateOutputSink(configuration);
        _disposables.Add(outputSink);
        if (outputSink is BufferedForwarderSink sink)
        {
            StartHealthReporter(configuration, sink);
        }

        var bindings = CreateBindings(configuration);
        var runtime = new AgentRuntime(bindings, outputSink, warn: msg =>
            logger.LogWarning("{Warning}", msg));

        return Task.Run(() => runtime.Run(stoppingToken), stoppingToken);
    }


    private IReadOnlyList<ProfileBinding> CreateForwarderCollectorBindings(ForwarderDaemonConfiguration configuration)
    {
        var executor = new PassthroughProfileExecutor();
        _disposables.Add(executor);
        ConfiguredForwarderCollectorInput(logger, configuration.Id, configuration.InputConf.Address, configuration.InputConf.Port, null);

        return [new ProfileBinding(
            new ForwarderInput(new ForwarderInputConfiguration {
                Address = configuration.InputConf.Address,
                Port = configuration.InputConf.Port,
                UseTls = configuration.InputConf.UseTls,
                ServerCertificatePath = configuration.InputConf.ServerCertificatePath,
                ServerCertificatePassword = configuration.InputConf.ServerCertificatePassword
            }, $"{configuration.Id}-forwarder-collector"),
            CreateCollectorProfile(),
            executor)];
    }

    private static ResourceProfile CreateCollectorProfile() => new() {
        Id = "daemon-forwarder-collector.passthrough",
        Name = "Daemon FORWARDER collector passthrough",
        Version = "1.0.0",
        Resource = new ResourceDescriptor {
            Platform = "portable",
            Family = "forwarder"
        },
        Input = new ResourceInputContract {
            Table = "Source"
        },
        Output = new ResourceOutputContract {
            Format = "ndjson",
            PreserveOriginalFieldNames = true,
            PreserveRawEvent = true,
            MetadataEnvelope = true,
            EventEnvelope = true,
            OnNoMatch = "emit"
        }
    };

    private void ApplyResourceQuotas(ForwarderDaemonConfiguration configuration)
    {
        if (configuration.ResourceQuotas.CpuPercent is not { } cpuPercent)
        {
            return;
        }

#if WINDOWS
        var limiter = WindowsJobObjectResourceLimiter.ApplyToCurrentProcess(cpuPercent);
        _disposables.Add(limiter);
        logger.LogInformation("Applied Windows job object CPU quota of {CpuPercent}% to the agent daemon process.", cpuPercent);
#else
        logger.LogWarning("Ignoring configured resourceQuotas.cpuPercent={CpuPercent} because CPU quotas are only supported by the Windows build.", cpuPercent);
#endif
    }

    public override void Dispose()
    {
        for (var index = _disposables.Count - 1; index >= 0; index--)
        {
            _disposables[index].Dispose();
        }

        base.Dispose();
    }

    private IReadOnlyList<ProfileBinding> CreateBindings(ForwarderDaemonConfiguration configuration)
    {
        if (configuration.Pipeline.Input.Mode.Equals("forwarder", StringComparison.OrdinalIgnoreCase))
        {
            return CreateForwarderCollectorBindings(configuration);
        }

        var loadResult = new YamlResourceProfileLoader().LoadDirectory(configuration.ProfilesPath);
        foreach (var warning in loadResult.Warnings)
        {
            logger.LogWarning("{Warning}", warning);
        }

        if (loadResult.Errors.Count > 0)
        {
            throw new InvalidDataException($"Failed to load daemon profiles from '{configuration.ProfilesPath}': {string.Join("; ", loadResult.Errors)}");
        }

        var profiles = loadResult.Profiles
            .Where(profile => profile.Enabled)
            .Where(IsProfileForCurrentPlatform)
            .ToList();

        var bindings = new List<ProfileBinding>(profiles.Count);
        foreach (var profile in profiles)
        {
            if (!IsProfileConditionSatisfied(profile, out var conditionWarning))
            {
                if (conditionWarning is null)
                {
                    ProfileConditionNotSatisfied(logger, profile.Id, null);
                }
                else
                {
                    logger.LogWarning("{Warning}", conditionWarning);
                }

                continue;
            }

            if (ShouldSkipUnavailableWindowsResource(profile, out var resourceWarning))
            {
                logger.LogWarning("{Warning}", resourceWarning);
                continue;
            }

            var executor = new ResourceKqlProfileExecutor();
            _disposables.Add(executor);
            ConfiguredProfile(logger, profile.Id, profile.Resource.Family, configuration.Id, null);
            bindings.Add(new ProfileBinding(CreateInput(profile), profile, executor));
        }

        if (bindings.Count == 0)
        {
            logger.LogWarning("Agent daemon {AgentId} has no runnable profiles after filtering unavailable resources.", configuration.Id);
        }

        return bindings;
    }

#if WINDOWS
    private static bool ShouldSkipUnavailableWindowsResource(ResourceProfile profile, out string warning)
    {
        warning = string.Empty;
        var validationResult = profile.Resource.Family.ToLowerInvariant() switch {
            "eventlog" => WindowsResourceValidator.ValidateEventLog(profile),
            "etw" => WindowsResourceValidator.ValidateEtw(profile),
            _ => WindowsResourceValidationResult.Valid
        };

        if (validationResult.IsValid)
        {
            return false;
        }

        warning = validationResult.WarningMessage ?? validationResult.ErrorMessage ?? string.Empty;
        return true;
    }
#else

    private static bool ShouldSkipUnavailableWindowsResource(ResourceProfile profile, out string warning)
    {
        warning = string.Empty;
        return false;
    }

#endif

    private bool IsProfileConditionSatisfied(ResourceProfile profile, out string? warning) =>
        _prefilter.IsSatisfied(profile, out warning);

    private static bool IsProfileForCurrentPlatform(ResourceProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Resource.Platform))
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            return profile.Resource.Platform.Equals("windows", StringComparison.OrdinalIgnoreCase);
        }

        if (OperatingSystem.IsLinux())
        {
            return profile.Resource.Platform.Equals("linux", StringComparison.OrdinalIgnoreCase);
        }

        return profile.Resource.Platform.Equals("portable", StringComparison.OrdinalIgnoreCase);
    }

    private ISourceInput CreateInput(ResourceProfile profile)
    {
        var family = profile.Resource.Family.ToLowerInvariant();
        return family switch {
            "syslog" => new SyslogFileTailInput("/var/log/auth.log", profile.Id),
            "auditd" => new AuditdFileInput("/var/log/audit/audit.log", profile.Id),
#if WINDOWS
            "eventlog" => new WindowsEventLogInput(profile.Resource.Channel ?? throw new ArgumentException($"profile '{profile.Id}' requires resource.channel for eventlog.")),
            "etl" => new EtlFileInput(profile.Resource.Channel ?? throw new ArgumentException($"profile '{profile.Id}' requires resource.channel for etl."), warn: LogWarning),
            "etw" => CreateEtwInput(profile),
#else
            "eventlog" or "evtx" or "etl" or "etw" => throw new PlatformNotSupportedException($"{family} is available from the net10.0-windows build."),
#endif
            _ => throw new ArgumentException($"profile '{profile.Id}' has unknown resource.family '{profile.Resource.Family}'.")
        };
    }

    private void LogWarning(string message) => logger.LogWarning("{Warning}", message);

#if WINDOWS
    private ISourceInput CreateEtwInput(ResourceProfile profile)
    {
        var session = profile.Resource.Session ?? throw new ArgumentException($"profile '{profile.Id}' requires resource.session for etw.");
        return profile.Resource.Mode.Equals("managed", StringComparison.OrdinalIgnoreCase)
            ? new ManagedEtwSessionInput(
                session,
                profile.Resource.Provider ?? throw new ArgumentException($"profile '{profile.Id}' requires resource.provider for managed etw."),
                profile.Resource,
                warn: LogWarning)
            : new EtwSessionInput(session, profile.Resource, warn: LogWarning);
    }
#endif

    private IOutputWriter CreateOutputSink(ForwarderDaemonConfiguration configuration) =>
        configuration.Pipeline.Output.Mode.ToLowerInvariant() switch {
            "console" => new ConsoleNdjsonSink(prettyPrint: configuration.Pipeline.Output.PrettyPrint),
            "file" => new NdjsonFileSink(configuration.Pipeline.Output.File!, prettyPrint: configuration.Pipeline.Output.PrettyPrint),
            _ => CreateForwarderSink(configuration)
        };

    private BufferedForwarderSink CreateForwarderSink(ForwarderDaemonConfiguration configuration)
    {
        var (endpoints, useTls) = configuration.Tunnel.Enabled
            ? StartForwarderTunnel(configuration)
            : (configuration.Transport.Endpoints, configuration.Transport.UseTls);

        var clientCertificates = configuration.Tunnel.Enabled
            ? null
            : CreateClientCertificates(configuration.Transport.Tls);
        var transportOptions = configuration.Transport.ToForwarderOptions(clientCertificates, endpoints, useTls);

        return new BufferedForwarderSink(
            configuration.Buffer.ToBufferOptions(),
            new ForwarderTransport(transportOptions),
            configuration.Buffer.ToRetryConfiguration());
    }

    private void StartHealthReporter(ForwarderDaemonConfiguration configuration, BufferedForwarderSink sink)
    {
        if (configuration.Diagnostics.IntervalSeconds is { } intervalSeconds)
        {
            IOutputWriter diagnosticSink = string.IsNullOrWhiteSpace(configuration.Diagnostics.File)
                ? new ConsoleNdjsonSink(prettyPrint: configuration.Diagnostics.PrettyPrint)
                : new NdjsonFileSink(configuration.Diagnostics.File, prettyPrint: configuration.Diagnostics.PrettyPrint);
            _disposables.Add(diagnosticSink);
            _disposables.Add(new ForwarderHealthReporter(
                sink,
                diagnosticSink,
                new CollectorObservationMetadata { AgentId = configuration.Id, HostId = Environment.MachineName },
                TimeSpan.FromSeconds(intervalSeconds)));
        }

        if (!string.IsNullOrWhiteSpace(configuration.Diagnostics.SqliteFile))
        {
            var sqliteIntervalSeconds = configuration.Diagnostics.SqliteIntervalSeconds ?? 3;
            var sqliteSink = new SqliteMetricsStateWriter(configuration.Diagnostics.SqliteFile);
            _disposables.Add(sqliteSink);
            _disposables.Add(new ForwarderHealthReporter(
                sink,
                sqliteSink,
                new CollectorObservationMetadata { AgentId = configuration.Id, HostId = Environment.MachineName },
                TimeSpan.FromSeconds(sqliteIntervalSeconds)));
        }
    }

    private (IReadOnlyList<ForwarderEndpoint> Endpoints, bool UseTls) StartForwarderTunnel(ForwarderDaemonConfiguration configuration)
    {
        if (configuration.Transport.Tls.CertificateValidation == CertificateValidationMode.Disabled)
        {
            throw new InvalidOperationException("FORWARDER tunnel mode does not support disabled server certificate validation. Use SystemTrust or Thumbprint validation for tunneled TLS.");
        }

        var tunnel = new TcpTunnel(new TcpTunnelOptions {
            ListenEndpoint = new TunnelEndpoint {
                Host = configuration.Tunnel.Listen.Host,
                Port = configuration.Tunnel.Listen.Port
            },
            Endpoints = configuration.Transport.Endpoints
                .ConvertAll(endpoint => new TunnelEndpoint { Host = endpoint.Host, Port = endpoint.Port })
,
            UseTls = configuration.Transport.UseTls,
            CertificateProvider = CreateTunnelCertificateProvider(configuration.Transport.Tls),
            ServerCertificateValidationCallback = CreateServerCertificateValidationCallback(configuration.Transport.Tls)
        });
        tunnel.Start();
        _disposables.Add(tunnel);

        var localEndpoint = new ForwarderEndpoint {
            Host = configuration.Tunnel.Listen.Host,
            Port = configuration.Tunnel.Listen.Port
        };
        return ([localEndpoint], false);
    }

    private static ITunnelCertificateProvider? CreateTunnelCertificateProvider(ForwarderTlsConfiguration tls) =>
        !tls.ClientCertificateEnabled || string.IsNullOrWhiteSpace(tls.ClientCertificatePath)
            ? null
            : new FileTunnelCertificateProvider(new TunnelCertificateOptions {
                Enabled = tls.ClientCertificateEnabled,
                CertificatePath = tls.ClientCertificatePath,
                CertificatePassword = tls.ClientCertificatePassword
            });

    private static System.Security.Cryptography.X509Certificates.X509CertificateCollection? CreateClientCertificates(ForwarderTlsConfiguration tls)
    {
        var provider = CreateTunnelCertificateProvider(tls);
        if (provider is null)
        {
            return null;
        }

        var lease = provider.GetCurrentCertificateAsync().AsTask().GetAwaiter().GetResult();
        return lease is null ? null : [lease.Certificate];
    }

    private static System.Net.Security.RemoteCertificateValidationCallback? CreateServerCertificateValidationCallback(ForwarderTlsConfiguration tls) =>
        tls.CertificateValidation switch {
            CertificateValidationMode.Thumbprint => (_, certificate, _, _) =>
                certificate is not null
                && tls.AllowedServerCertificateThumbprints.Contains(
                    certificate.GetCertHashString(),
                    StringComparer.OrdinalIgnoreCase),
            _ => null
        };
}

internal sealed class PassthroughProfileExecutor : IProfileExecutor
{
    public IObservable<ResourceOutputRecord> Execute(
        IObservable<SourceEvent> source,
        ResourceProfile profile,
        CancellationToken cancellationToken = default) =>
        System.Reactive.Linq.Observable.Select(
            source,
            sourceEvent => ResourceOutputRecord.FromSource(sourceEvent, profile.Id, profile.Version));

    public void Dispose()
    {
    }
}
