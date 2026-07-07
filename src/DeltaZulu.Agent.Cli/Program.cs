using System.Net;
using System.Text.Json;
using DeltaZulu.Agent.Runtime;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Ndjson;
using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Pipeline.Inputs.Auditd;
using DeltaZulu.Pipeline.Inputs.Files;
using DeltaZulu.Pipeline.Inputs.Syslog;
using DeltaZulu.Agent.Filter.Kql;
using DeltaZulu.Pipeline.Outputs.Ndjson;
using DeltaZulu.Agent.Filter.Prefilter;

#if WINDOWS
using DeltaZulu.Pipeline.Core.Windows;
using DeltaZulu.Pipeline.Inputs.Windows;
#endif

namespace DeltaZulu.Agent.Cli;

internal static partial class Program
{
    private static readonly ResourceProfilePrefilter Prefilter = new(DefaultConditionEvaluators.ForCurrentPlatform());

    private const string Version = "0.1.0";

    public static int Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        if (args[0] is "--version" or "version")
        {
            Console.WriteLine($"DeltaZulu.Agent {Version}");
            Console.Out.Flush();
            return 0;
        }

        try
        {
            if (IsModeCommand(args[0]))
            {
                return RunModeCommand(args);
            }

            if (IsServiceCommand(args[0]))
            {
                return RunServiceCommand(args);
            }

            Console.Error.WriteLine($"error: unknown dzagentctl command '{args[0]}'. Use --help for controller modes.");
            Console.Error.Flush();
            return 1;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex)
        {
            LogPipelineError(ex);
            Console.Error.Flush();
            return 1;
        }
    }

    private static int RunPipelinePlan(string[] args)
    {
        try
        {
            var plan = CliPlan.Parse(args);
            if (!ValidateResources(plan))
            {
                return 1;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            using var sink = CreateSink(plan);

            var bindings = CreateBindings(plan);
            try
            {
                var runtime = new AgentRuntime(bindings, sink, warn: msg => {
                    Console.Error.WriteLine($"warning: {msg}");
                    Console.Error.Flush();
                });
                var result = runtime.Run(cts.Token);

                if (!result.Success)
                {
                    LogPipelineError(result.Error!);
                    return 1;
                }

                if (result.Degraded)
                {
                    foreach (var warning in result.Warnings!)
                    {
                        Console.Error.WriteLine($"warning: {warning}");
                    }
                    Console.Error.Flush();
                }

                return 0;
            }
            finally
            {
                DisposeExecutors(bindings);
            }
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex)
        {
            LogPipelineError(ex);
            Console.Error.Flush();
            return 1;
        }
    }

    private static IReadOnlyList<ProfileBinding> CreateBindings(CliPlan plan)
    {
        if (plan.IsProfileMode)
        {
            var profiles = FilterUnavailableResources(plan, LoadProfiles(plan.Option("--profile")!));
            if (profiles.Count == 0)
            {
                throw new InvalidOperationException("No enabled profiles were found.");
            }

            return profiles
                .Select(profile => new ProfileBinding(
                    CreateInput(plan, profile),
                    profile,
                    new ResourceKqlProfileExecutor()))
                .ToArray();
        }

        var executor = new ResourceKqlProfileExecutor();
        var inlineKql = plan.Option("--kql") ?? plan.Option("--query") ?? plan.Option("-q");
        var profilePath = plan.Option("--profile");
        ResourceProfile profile2;

        if (!string.IsNullOrWhiteSpace(profilePath) && !string.IsNullOrWhiteSpace(inlineKql))
        {
            throw new ArgumentException("Use either --profile <profile.yaml> or --kql <query>, not both.");
        }

        if (!string.IsNullOrWhiteSpace(profilePath))
        {
            profile2 = new YamlResourceProfileLoader().LoadFile(profilePath);
        }
        else if (!string.IsNullOrWhiteSpace(inlineKql))
        {
            profile2 = CreateInlineProfile(plan, inlineKql);
        }
        else
        {
            profile2 = CreatePassthroughProfile(plan);
        }

        return [new ProfileBinding(CreateInput(plan), profile2, executor)];
    }

    private static ResourceProfile CreateInlineProfile(CliPlan plan, string query) => new() {
        SchemaVersion = 1,
        Id = plan.Option("--resource-id") ?? "cli.inline",
        Name = plan.Option("--resource-name") ?? "Inline CLI query",
        Version = "1.0.0",
        Resource = new ResourceDescriptor {
            Platform = plan.Option("--platform") ?? "local",
            Family = plan.InputCommand ?? "inline"
        },
        Input = new ResourceInputContract {
            Table = plan.Option("--table") ?? "Source",
            Schema = plan.Option("--schema") ?? string.Empty
        },
        Filter = new ResourceFilter {
            Language = "kql",
            Query = query
        },
        Output = new ResourceOutputContract {
            Format = "ndjson",
            PreserveOriginalFieldNames = true
        }
    };

    private static ISourceInput CreateInput(CliPlan plan) => CreateInput(plan, null);

    private static ISourceInput CreateInput(CliPlan plan, ResourceProfile? profile)
    {
        var inputCommand = plan.InputCommand ?? ProfileFamilyToInputCommand(profile)
            ?? throw new ArgumentException("input command is required when no profile resource family is available.");

        return inputCommand switch {
            "syslog" => new SyslogFileTailInput(plan.InputArgument ?? throw new ArgumentException("syslog profiles require a target <file> argument.")),
            "syslogserver" => new TcpSyslogInput(IPAddress.Parse(plan.Option("--address") ?? "0.0.0.0"), int.Parse(plan.Option("--port") ?? "514")),
            "fifo" => new FifoSyslogInput(plan.InputArgument ?? throw new ArgumentException("fifo requires a target <path> argument.")),
            "csv" => new CsvFileInput(plan.InputArgument ?? throw new ArgumentException("csv profiles require a target <file.csv> argument.")),
            "auditd" => new AuditdFileInput(plan.InputArgument ?? throw new ArgumentException("auditd profiles require a target <file> argument.")),
#if WINDOWS
            "eventlog" => new WindowsEventLogInput(plan.InputArgument ?? profile?.Resource.Channel ?? throw new ArgumentException("eventlog profiles require resource.channel or a target <logname> argument.")),
            "evtx" => new EvtxFileInput(plan.InputArgument ?? throw new ArgumentException("evtx requires <file.evtx>")),
            "etl" => new EtlFileInput(plan.InputArgument ?? throw new ArgumentException("etl requires <file.etl>"), warn: WarnStderr),
            "etw" => new EtwSessionInput(plan.InputArgument ?? profile?.Resource.Session ?? throw new ArgumentException("etw requires <session>"), profile?.Resource, warn: WarnStderr),
#else
            "eventlog" or "evtx" or "etl" or "etw" => throw new PlatformNotSupportedException($"{inputCommand} is available from the net10.0-windows build."),
#endif
            _ => throw new ArgumentException($"unknown input command '{inputCommand}'")
        };
    }

    private static void WarnStderr(string message)
    {
        Console.Error.WriteLine($"warning: {message}");
        Console.Error.Flush();
    }

    private static ResourceProfile CreatePassthroughProfile(CliPlan plan) => new() {
        SchemaVersion = 1,
        Id = plan.Option("--resource-id") ?? "cli.passthrough",
        Name = "Passthrough",
        Version = "1.0.0",
        Resource = new ResourceDescriptor {
            Platform = plan.Option("--platform") ?? "local",
            Family = plan.InputCommand ?? "inline"
        },
        Input = new ResourceInputContract {
            Table = plan.Option("--table") ?? "Source",
            Schema = plan.Option("--schema") ?? string.Empty
        },
        Output = new ResourceOutputContract {
            Format = "ndjson",
            PreserveOriginalFieldNames = true
        }
    };

    private static IOutputWriter CreateSink(CliPlan plan) => string.IsNullOrWhiteSpace(plan.OutputPath)
        ? new ConsoleNdjsonSink()
        : new NdjsonFileSink(plan.OutputPath);

    private static void DisposeExecutors(IReadOnlyList<ProfileBinding> bindings)
    {
        foreach (var binding in bindings)
        {
            binding.Executor.Dispose();
        }
    }

    private static IReadOnlyList<ResourceProfile> FilterUnavailableResources(CliPlan plan, IReadOnlyList<ResourceProfile> profiles)
    {
#if WINDOWS
        var availableProfiles = new List<ResourceProfile>(profiles.Count);
        foreach (var profile in profiles)
        {
            if (!ShouldValidateWindowsResource(plan, profile))
            {
                availableProfiles.Add(profile);
                continue;
            }

            var validationResult = ValidateWindowsResource(plan, profile);
            if (validationResult.IsValid)
            {
                availableProfiles.Add(profile);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(validationResult.ErrorMessage))
            {
                throw new InvalidOperationException(validationResult.ErrorMessage);
            }

            // ValidateResources already emitted the optional-profile warning; keep this execution filter silent.
        }

        return availableProfiles;
#else
        return profiles;
#endif
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help";

    private static bool IsProfileConditionSatisfied(ResourceProfile profile)
    {
        var satisfied = Prefilter.IsSatisfied(profile, out var warning);
        if (!string.IsNullOrWhiteSpace(warning))
        {
            Console.Error.WriteLine($"warning: {warning}");
            Console.Error.Flush();
        }

        return satisfied;
    }

    private static IReadOnlyList<ResourceProfile> LoadProfiles(string path)
    {
        var loader = new YamlResourceProfileLoader();
        if (Directory.Exists(path))
        {
            var result = loader.LoadDirectory(path);
            LogProfileLoadWarnings(result.Warnings);
            if (result.Errors.Count > 0)
            {
                throw new InvalidDataException(string.Join(Environment.NewLine, result.Errors));
            }

            return result.Profiles.Where(profile => profile.Enabled && IsProfileConditionSatisfied(profile)).ToList();
        }

        var profile = loader.LoadFile(path);
        return profile.Enabled && IsProfileConditionSatisfied(profile) ? [profile] : [];
    }

    private static void LogPipelineError(Exception exception)
    {
        Console.Error.WriteLine("error: unhandled pipeline exception");
        Console.Error.WriteLine(exception);
        Console.Error.Flush();
    }

    private static void LogProfileLoadWarnings(IEnumerable<string> warnings)
    {
        foreach (var warning in warnings)
        {
            Console.Error.WriteLine($"warning: {warning}");
        }

        Console.Error.Flush();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
Usage:
  dzagentctl --tui [--profiles <profiles-dir>]
  dzagentctl --metrics [--sqlite <path>]
  dzagentctl --tail "<query>" <path>
  dzagentctl start|stop|restart|status|reload [-v] [--service <name>]

Controller modes:
  --tui                    Open the Terminal.Gui-backed local KQL editor TUI with built-in local resource schemas.
                           Use :schemas inside the TUI to discover queryable local tables.
  --metrics                Open the Terminal.Gui-backed agent metrics TUI from the daemon SQLite state (agent metrics only, not system htop).
  --tail "<query>" <path>  Open the Terminal.Gui-backed tail preflight and query file-backed resources with KQL; unknown formats fall back to a Lines table with a single line column.
  start/stop/restart/status/reload
                           Control the dzagentd service, systemctl-style.

Examples:
  dzagentctl --tui
  # inside --tui: Processes\n| project name, memMB=workingSet/1024/1024\n| order by memMB desc\n| take 1
  dzagentctl --metrics [--sqlite <path>]
  dzagentctl --tail "Syslog | where Severity == 'error'" /var/log/syslog
  dzagentctl status -v
""");
        Console.Out.Flush();
    }

    private static string? ProfileFamilyToInputCommand(ResourceProfile? profile) => profile?.Resource.Family.ToLowerInvariant() switch {
        "syslog" => "syslog",
        "syslogserver" => "syslogserver",
        "fifo" => "fifo",
        "csv" => "csv",
        "auditd" => "auditd",
        "eventlog" => "eventlog",
        "windows.eventlog" => "eventlog",
        "evtx" => "evtx",
        "etl" => "etl",
        "etw" => "etw",
        _ => null
    };

    private static bool ValidateResources(CliPlan plan)
    {
#if WINDOWS
        if (plan.IsProfileMode)
        {
            foreach (var profile in LoadProfiles(plan.Option("--profile")!))
            {
                if (!ShouldValidateWindowsResource(plan, profile))
                {
                    continue;
                }

                var validationResult = ValidateWindowsResource(plan, profile);
                if (validationResult.IsValid)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(validationResult.WarningMessage))
                {
                    Console.Error.WriteLine($"warning: {validationResult.WarningMessage}");
                    Console.Error.Flush();
                    continue;
                }

                Console.Error.WriteLine($"error: {validationResult.ErrorMessage}");
                Console.Error.Flush();
                return false;
            }
        }
        else if (plan.InputCommand?.Equals("eventlog", StringComparison.OrdinalIgnoreCase) == true)
        {
            var profile = CreatePassthroughProfile(plan);
            profile.Resource.Channel = plan.InputArgument ?? throw new ArgumentException("eventlog requires <logname>");
            var validationResult = WindowsResourceValidator.ValidateEventLog(profile, plan.InputArgument);
            if (!validationResult.IsValid)
            {
                Console.Error.WriteLine($"error: {validationResult.ErrorMessage ?? validationResult.WarningMessage}");
                Console.Error.Flush();
                return false;
            }
        }
        else if (plan.InputCommand?.Equals("etw", StringComparison.OrdinalIgnoreCase) == true)
        {
            var profile = CreatePassthroughProfile(plan);
            profile.Resource.Session = plan.InputArgument ?? throw new ArgumentException("etw requires <session>");
            var validationResult = WindowsResourceValidator.ValidateEtw(profile, plan.InputArgument);
            if (!validationResult.IsValid)
            {
                Console.Error.WriteLine($"error: {validationResult.ErrorMessage ?? validationResult.WarningMessage}");
                Console.Error.Flush();
                return false;
            }
        }
#endif

        return true;
    }

#if WINDOWS
    private static bool ShouldValidateWindowsResource(CliPlan plan, ResourceProfile profile)
    {
        var command = plan.InputCommand ?? ProfileFamilyToInputCommand(profile);
        return command is not null
            && (command.Equals("eventlog", StringComparison.OrdinalIgnoreCase)
                || command.Equals("etw", StringComparison.OrdinalIgnoreCase));
    }

    private static WindowsResourceValidationResult ValidateWindowsResource(CliPlan plan, ResourceProfile profile)
    {
        var command = plan.InputCommand ?? ProfileFamilyToInputCommand(profile);
        return command?.ToLowerInvariant() switch
        {
            "eventlog" => WindowsResourceValidator.ValidateEventLog(profile, plan.InputArgument),
            "etw" => WindowsResourceValidator.ValidateEtw(profile, plan.InputArgument),
            _ => WindowsResourceValidationResult.Valid
        };
    }
#endif
}
