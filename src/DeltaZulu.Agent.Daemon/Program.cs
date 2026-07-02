using DeltaZulu.Agent.Runtime;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Observability;
using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Pipeline.Inputs.Auditd;
using DeltaZulu.Pipeline.Inputs.Relp;
using DeltaZulu.Pipeline.Inputs.Syslog;
using DeltaZulu.Pipeline.Kql;
using DeltaZulu.Pipeline.Outputs.Ndjson;
using DeltaZulu.Pipeline.Outputs.Relp;
using DeltaZulu.Pipeline.Tunnel;
using DeltaZulu.Platform.Prefilter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

This executable is service-shaped from the start: it hosts configured DeltaZulu daemon pipelines and has no query, table, JSON export, or schema commands. Run it with config/dzagent.yaml for agent forwarding or config/dzcollector.yaml for local RELP receiver validation.
""");
        Console.Out.Flush();
    }
}

internal sealed class ForwarderDaemonService(string configPath, ILogger<ForwarderDaemonService> logger) : BackgroundService
{
    private readonly List<IDisposable> _disposables = [];
    private readonly ResourceProfilePrefilter _prefilter = new(DefaultConditionEvaluators.ForCurrentPlatform());

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = new YamlForwarderDaemonConfigurationLoader().LoadFile(configPath);
        logger.LogInformation("Starting DeltaZulu agent daemon {AgentId} from {ConfigPath}.", configuration.Id, configPath);
        ApplyResourceQuotas(configuration);

        var outputSink = CreateOutputSink(configuration);
        _disposables.Add(outputSink);
        if (outputSink is BufferedRelpSink relpSink)
        {
            StartHealthReporter(configuration, relpSink);
        }

        var bindings = CreateBindings(configuration);
        var runtime = new AgentRuntime(bindings, outputSink, warn: msg =>
            logger.LogWarning("{Warning}", msg));

        return Task.Run(() => runtime.Run(stoppingToken), stoppingToken);
    }


    private IReadOnlyList<ProfileBinding> CreateRelpCollectorBindings(ForwarderDaemonConfiguration configuration)
    {
        var executor = new PassthroughProfileExecutor();
        _disposables.Add(executor);
        logger.LogInformation(
            "Configured RELP collector input for daemon {AgentId} on {Address}:{Port}.",
            configuration.Id,
            configuration.RelpInput.Address,
            configuration.RelpInput.Port);

        return [new ProfileBinding(
            new RelpInput(new RelpInputConfiguration {
                Address = configuration.RelpInput.Address,
                Port = configuration.RelpInput.Port,
                UseTls = configuration.RelpInput.UseTls,
                ServerCertificatePath = configuration.RelpInput.ServerCertificatePath,
                ServerCertificatePassword = configuration.RelpInput.ServerCertificatePassword
            }, $"{configuration.Id}-relp-collector"),
            CreateCollectorProfile(),
            executor)];
    }

    private static ResourceProfile CreateCollectorProfile() => new() {
        Id = "daemon-relp-collector.passthrough",
        Name = "Daemon RELP collector passthrough",
        Version = "1.0.0",
        Resource = new ResourceDescriptor {
            Platform = "portable",
            Family = "relp"
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
        if (configuration.Pipeline.Input.Mode.Equals("relp", StringComparison.OrdinalIgnoreCase))
        {
            return CreateRelpCollectorBindings(configuration);
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
                logger.LogWarning("{Warning}", conditionWarning ?? $"Profile '{profile.Id}' condition is not satisfied.");
                continue;
            }

            if (ShouldSkipUnavailableWindowsResource(profile, out var resourceWarning))
            {
                logger.LogWarning("{Warning}", resourceWarning);
                continue;
            }

            var executor = new ResourceKqlProfileExecutor();
            _disposables.Add(executor);
            logger.LogInformation("Configured profile {ProfileId} ({Family}) for daemon {AgentId}.", profile.Id, profile.Resource.Family, configuration.Id);
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
            _ => CreateRelpSink(configuration)
        };

    private BufferedRelpSink CreateRelpSink(ForwarderDaemonConfiguration configuration)
    {
        var (endpoints, useTls) = configuration.Tunnel.Enabled
            ? StartRelpTunnel(configuration)
            : ((IReadOnlyList<RelpEndpoint>)configuration.Relp.Endpoints, configuration.Relp.UseTls);

        var primaryEndpoint = endpoints[0];
        return new BufferedRelpSink(configuration.Buffer.ToBufferOptions(), new RelpForwarderTransport(new RelpForwarderOptions {
            Host = primaryEndpoint.Host,
            Port = primaryEndpoint.Port,
            Endpoints = endpoints,
            UseTls = useTls,
            ClientCertificates = configuration.Tunnel.Enabled ? null : CreateClientCertificates(configuration.Relp.Tls),
            CertificateValidation = configuration.Relp.Tls.CertificateValidation,
            AllowedServerCertificateThumbprints = configuration.Relp.Tls.AllowedServerCertificateThumbprints,
            CertificateExpiryWarningDays = configuration.Relp.Tls.CertificateExpiryWarningDays
        }));
    }

    private void StartHealthReporter(ForwarderDaemonConfiguration configuration, BufferedRelpSink relpSink)
    {
        if (configuration.Diagnostics.IntervalSeconds is not { } intervalSeconds)
        {
            return;
        }

        IOutputWriter diagnosticSink = string.IsNullOrWhiteSpace(configuration.Diagnostics.File)
            ? new ConsoleNdjsonSink(prettyPrint: configuration.Diagnostics.PrettyPrint)
            : new NdjsonFileSink(configuration.Diagnostics.File, prettyPrint: configuration.Diagnostics.PrettyPrint);
        _disposables.Add(diagnosticSink);
        _disposables.Add(new RelpHealthReporter(
            relpSink,
            diagnosticSink,
            new CollectorObservationMetadata { AgentId = configuration.Id, HostId = Environment.MachineName },
            TimeSpan.FromSeconds(intervalSeconds)));
    }

    private (IReadOnlyList<RelpEndpoint> Endpoints, bool UseTls) StartRelpTunnel(ForwarderDaemonConfiguration configuration)
    {
        var tunnel = new TcpTunnel(new TcpTunnelOptions {
            ListenEndpoint = new TunnelEndpoint {
                Host = configuration.Tunnel.Listen.Host,
                Port = configuration.Tunnel.Listen.Port
            },
            Endpoints = configuration.Relp.Endpoints
                .ConvertAll(endpoint => new TunnelEndpoint { Host = endpoint.Host, Port = endpoint.Port })
,
            UseTls = configuration.Relp.UseTls,
            CertificateProvider = CreateTunnelCertificateProvider(configuration.Relp.Tls),
            ServerCertificateValidationCallback = CreateServerCertificateValidationCallback(configuration.Relp.Tls)
        });
        tunnel.Start();
        _disposables.Add(tunnel);

        var localEndpoint = new RelpEndpoint {
            Host = configuration.Tunnel.Listen.Host,
            Port = configuration.Tunnel.Listen.Port
        };
        return ([localEndpoint], false);
    }

    private static ITunnelCertificateProvider? CreateTunnelCertificateProvider(RelpTlsConfiguration tls) =>
        !tls.ClientCertificateEnabled || string.IsNullOrWhiteSpace(tls.ClientCertificatePath)
            ? null
            : new FileTunnelCertificateProvider(new TunnelCertificateOptions {
                Enabled = tls.ClientCertificateEnabled,
                CertificatePath = tls.ClientCertificatePath,
                CertificatePassword = tls.ClientCertificatePassword
            });

    private static System.Security.Cryptography.X509Certificates.X509CertificateCollection? CreateClientCertificates(RelpTlsConfiguration tls)
    {
        var provider = CreateTunnelCertificateProvider(tls);
        if (provider is null)
        {
            return null;
        }

        var lease = provider.GetCurrentCertificateAsync().AsTask().GetAwaiter().GetResult();
        return lease is null ? null : [lease.Certificate];
    }

    private static System.Net.Security.RemoteCertificateValidationCallback? CreateServerCertificateValidationCallback(RelpTlsConfiguration tls) =>
        tls.CertificateValidation switch {
            RelpCertificateValidationMode.Disabled => (_, _, _, _) => true,
            RelpCertificateValidationMode.Thumbprint => (_, certificate, _, _) =>
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
