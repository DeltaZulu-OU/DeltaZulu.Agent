using DeltaZulu.Agent.Application.Abstractions;
using DeltaZulu.Agent.Application.Runtime;
using DeltaZulu.Agent.Core.Observability;
using DeltaZulu.Agent.Forwarder;
using DeltaZulu.Agent.Inputs.Auditd;
using DeltaZulu.Agent.Inputs.Files;
using DeltaZulu.Agent.Inputs.Syslog;
using DeltaZulu.Agent.Kql;
using DeltaZulu.Agent.Outputs.Ndjson;
using DeltaZulu.Agent.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography.X509Certificates;

#if WINDOWS
using DeltaZulu.Agent.Inputs.Windows;
#endif

namespace DeltaZulu.Agent.Daemon;

internal static class Program
{
    private const string DefaultConfigPath = "config/dzagentd.yaml";

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
                throw new ArgumentException($"unknown daemon option '{token}'. Only --config is supported; this executable is forwarder-only.");
            }
        }

        return configPath;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
Usage:
  dzagentd --config <dzagentd.yaml>
  dzagentd <dzagentd.yaml>

This executable is service-shaped from the start: it hosts only the DeltaZulu forwarder pipeline and has no query, table, JSON export, or schema commands. It can run interactively during development, as a plain Linux process in containers or non-systemd environments, and under Windows Service Control Manager on Windows or systemd on Linux when those service managers are present.
""");
        Console.Out.Flush();
    }
}

internal sealed class ForwarderDaemonService(string configPath, ILogger<ForwarderDaemonService> logger) : BackgroundService
{
    private readonly List<IDisposable> _disposables = [];

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = new YamlForwarderDaemonConfigurationLoader().LoadFile(configPath);
        logger.LogInformation("Starting DeltaZulu agent daemon {AgentId} from {ConfigPath}.", configuration.Id, configPath);

        var forwarderSink = CreateForwarderSink(configuration);
        _disposables.Add(forwarderSink);
        StartHealthReporter(configuration, forwarderSink);

        var bindings = CreateBindings(configuration);
        var runtime = new AgentRuntime(bindings, forwarderSink, warn: msg =>
            logger.LogWarning("{Warning}", msg));

        return Task.Run(() => runtime.Run(stoppingToken), stoppingToken)
            .ContinueWith(task =>
            {
                if (task.Exception is not null)
                {
                    logger.LogError(task.Exception, "Agent daemon runtime failed.");
                }
            }, TaskScheduler.Default);
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
        var bindings = new List<ProfileBinding>(configuration.Sources.Count);
        foreach (var source in configuration.Sources)
        {
            var profile = LoadProfile(source);
            if (ShouldSkipDisabledWindowsEventLog(source, profile, out var warning))
            {
                logger.LogWarning("{Warning}", warning);
                continue;
            }

            var executor = new ResourceKqlProfileExecutor();
            _disposables.Add(executor);
            logger.LogInformation("Configured forwarder source {SourceId} ({Input}) for daemon {AgentId}.", source.Id, source.Input, configuration.Id);
            bindings.Add(new ProfileBinding(CreateInput(source, profile), profile, executor));
        }

        if (bindings.Count == 0)
        {
            logger.LogWarning("Agent daemon {AgentId} has no runnable sources after filtering unavailable resources.", configuration.Id);
        }

        return bindings;
    }

#if WINDOWS
    private static bool ShouldSkipDisabledWindowsEventLog(ForwarderDaemonSourceConfiguration source, ResourceProfile? profile, out string warning)
    {
        warning = string.Empty;
        if (!source.Input.Equals("eventlog", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var target = source.Target ?? profile?.Resource.Channel;
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        if (WindowsEventLogInput.TryResolveLogName(target, out _, out var errorMessage))
        {
            return false;
        }

        if (!WindowsEventLogInput.IsDisabledChannelError(errorMessage))
        {
            return false;
        }

        warning = $"Skipping daemon source '{source.Id}' because {errorMessage}";
        return true;
    }
#else
    private static bool ShouldSkipDisabledWindowsEventLog(ForwarderDaemonSourceConfiguration source, ResourceProfile? profile, out string warning)
    {
        warning = string.Empty;
        return false;
    }
#endif

    private static ResourceProfile LoadProfile(ForwarderDaemonSourceConfiguration source)
    {
        if (string.IsNullOrWhiteSpace(source.Profile))
        {
            return new ResourceProfile
            {
                SchemaVersion = 1,
                Id = $"{source.Id}.passthrough",
                Name = $"{source.Id} passthrough",
                Version = "1.0.0",
                Resource = new ResourceDescriptor { Family = source.Input },
                Input = new ResourceInputContract { Table = "Source" },
                Output = new ResourceOutputContract { Format = "ndjson", PreserveOriginalFieldNames = true }
            };
        }

        var profile = new YamlResourceProfileLoader().LoadFile(source.Profile);
        return profile.Enabled ? profile : throw new InvalidDataException($"Agent daemon source '{source.Id}' references disabled profile '{profile.Id}'.");
    }

    private static ISourceInput CreateInput(ForwarderDaemonSourceConfiguration source, ResourceProfile? profile)
    {
        var input = source.Input.ToLowerInvariant();
        var target = source.Target ?? profile?.Resource.Channel;
        return input switch
        {
            "syslog" => new SyslogFileTailInput(target ?? throw new ArgumentException($"source '{source.Id}' requires target for syslog.")),
            "syslogserver" => new TcpSyslogInput(IPAddress.Parse(source.Address ?? "0.0.0.0"), source.Port ?? 514),
            "fifo" => new FifoSyslogInput(target ?? throw new ArgumentException($"source '{source.Id}' requires target for fifo.")),
            "csv" => new CsvFileInput(target ?? throw new ArgumentException($"source '{source.Id}' requires target for csv.")),
            "auditd" => new AuditdFileInput(target ?? throw new ArgumentException($"source '{source.Id}' requires target for auditd.")),
#if WINDOWS
            "eventlog" => new WindowsEventLogInput(target ?? throw new ArgumentException($"source '{source.Id}' requires target or profile resource.channel for eventlog.")),
            "evtx" => new EvtxFileInput(target ?? throw new ArgumentException($"source '{source.Id}' requires target for evtx.")),
            "etl" => new EtlFileInput(target ?? throw new ArgumentException($"source '{source.Id}' requires target for etl.")),
            "etw" => new EtwSessionInput(target ?? throw new ArgumentException($"source '{source.Id}' requires target for etw.")),
#else
            "eventlog" or "evtx" or "etl" or "etw" => throw new PlatformNotSupportedException($"{input} is available from the net10.0-windows build."),
#endif
            _ => throw new ArgumentException($"source '{source.Id}' has unknown input '{source.Input}'.")
        };
    }

    private static BufferedForwarderSink CreateForwarderSink(ForwarderDaemonConfiguration configuration)
    {
        var endpoints = configuration.Relp.Endpoints;
        var primaryEndpoint = endpoints[0];
        return new BufferedForwarderSink(configuration.Buffer.ToBufferOptions(), new RelpForwarderTransport(new RelpForwarderOptions
        {
            Host = primaryEndpoint.Host,
            Port = primaryEndpoint.Port,
            Endpoints = endpoints,
            UseTls = configuration.Relp.UseTls,
            ClientCertificates = LoadClientCertificates(configuration.Relp.Tls),
            CertificateValidation = configuration.Relp.Tls.CertificateValidation,
            AllowedServerCertificateThumbprints = configuration.Relp.Tls.AllowedServerCertificateThumbprints,
            CertificateExpiryWarningDays = configuration.Relp.Tls.CertificateExpiryWarningDays
        }));
    }

    private void StartHealthReporter(ForwarderDaemonConfiguration configuration, BufferedForwarderSink forwarderSink)
    {
        if (configuration.Diagnostics.IntervalSeconds is not { } intervalSeconds)
        {
            return;
        }

        IOutputWriter diagnosticSink = string.IsNullOrWhiteSpace(configuration.Diagnostics.File)
            ? new ConsoleNdjsonSink()
            : new NdjsonFileSink(configuration.Diagnostics.File);
        _disposables.Add(diagnosticSink);
        _disposables.Add(new ForwarderHealthReporter(
            forwarderSink,
            diagnosticSink,
            new CollectorObservationMetadata { AgentId = configuration.Id, HostId = Environment.MachineName },
            TimeSpan.FromSeconds(intervalSeconds)));
    }

    private static X509CertificateCollection? LoadClientCertificates(ForwarderTlsConfiguration tls)
    {
        if (string.IsNullOrWhiteSpace(tls.ClientCertificatePath))
        {
            return null;
        }

        return
        [
            string.IsNullOrEmpty(tls.ClientCertificatePassword)
                ? X509CertificateLoader.LoadCertificateFromFile(tls.ClientCertificatePath)
                : X509CertificateLoader.LoadPkcs12FromFile(tls.ClientCertificatePath, tls.ClientCertificatePassword)
        ];
    }
}
