using System.Net;
using System.Text.Json;
using DeltaZulu.Agent.Runtime;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Ndjson;
using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Pipeline.Inputs.Auditd;
using DeltaZulu.Pipeline.Inputs.Files;
using DeltaZulu.Pipeline.Inputs.Syslog;
using DeltaZulu.Pipeline.Kql;
using DeltaZulu.Pipeline.Outputs.Ndjson;

#if WINDOWS
using DeltaZulu.Pipeline.Inputs.Windows;
#endif

namespace DeltaZulu.Agent.Cli;

internal static partial class Program
{
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
            if (IsSchemaCommand(args[0]))
            {
                ListSchemas(args);
                return 0;
            }

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

    private static List<ResourceSchemaDescription> CreateBuiltInSchemas() =>
    [
        new(
            "input.syslog",
            "Syslog file tail",
            Version,
            true,
            "built-in",
            "linux",
            "syslog",
            null,
            null,
            null,
            "Syslog",
            "RawMessage:string,ReceivedAt:datetime,SourceIpAddress:string,Priority:int,Facility:string,Severity:string,SyslogVersion:string,Timestamp:datetime,Hostname:string,AppName:string,ProcessName:string,ProcId:string,ProcessId:int,MsgId:string,StructuredData:string,Message:string,ExtractedData:dynamic,_metadata:dynamic"),
        new(
            "input.syslogserver",
            "TCP syslog listener",
            Version,
            true,
            "built-in",
            "linux",
            "syslog",
            null,
            null,
            null,
            "Syslog",
            "RawMessage:string,ReceivedAt:datetime,SourceIpAddress:string,Priority:int,Facility:string,Severity:string,SyslogVersion:string,Timestamp:datetime,Hostname:string,AppName:string,ProcessName:string,ProcId:string,ProcessId:int,MsgId:string,StructuredData:string,Message:string,ExtractedData:dynamic,_metadata:dynamic"),
        new(
            "input.fifo",
            "Linux FIFO syslog input",
            Version,
            true,
            "built-in",
            "linux",
            "syslog",
            null,
            null,
            null,
            "Syslog",
            "RawMessage:string,ReceivedAt:datetime,Priority:int,Facility:string,Severity:string,SyslogVersion:string,Timestamp:datetime,Hostname:string,AppName:string,ProcessName:string,ProcId:string,ProcessId:int,MsgId:string,StructuredData:string,Message:string,ExtractedData:dynamic,_metadata:dynamic"),
        new(
            "input.csv",
            "CSV file",
            Version,
            true,
            "built-in",
            "portable",
            "file",
            "csv",
            null,
            null,
            "Csv",
            "<csv headers are discovered from the file at runtime>,_metadata:dynamic"),
        new(
            "input.auditd",
            "Linux auditd file",
            Version,
            true,
            "built-in",
            "linux",
            "auditd",
            null,
            null,
            null,
            "Auditd",
            "source:string,ID:string,RawEvent:dynamic,SYSCALL:dynamic,EXECVE:dynamic,PATH:dynamic,SOCKADDR:dynamic,CWD:dynamic,PROCTITLE:dynamic,_metadata:dynamic"),
        new(
            "input.windows.eventlog",
            "Windows Event Log",
            Version,
            true,
            "built-in",
            "windows",
            "eventlog",
            null,
            null,
            null,
            "EventLog",
            "source:string,ProviderName:string,EventId:int,Channel:string,RecordId:long,Level:string,Keywords:string,MachineName:string,TimeCreated:datetime,EventData:dynamic,Message:string,RawEvent:dynamic,_metadata:dynamic"),
        new(
            "input.windows.etw",
            "Windows ETW/ETL",
            Version,
            true,
            "built-in",
            "windows",
            "etw",
            null,
            null,
            null,
            "Etw",
            "source:string,Payload:dynamic,_metadata:dynamic")
    ];

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
            "etl" => new EtlFileInput(plan.InputArgument ?? throw new ArgumentException("etl requires <file.etl>")),
            "etw" => new EtwSessionInput(plan.InputArgument ?? throw new ArgumentException("etw requires <session>")),
#else
            "eventlog" or "evtx" or "etl" or "etw" => throw new PlatformNotSupportedException($"{inputCommand} is available from the net10.0-windows build."),
#endif
            _ => throw new ArgumentException($"unknown input command '{inputCommand}'")
        };
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
            if (!ShouldValidateWindowsEventLog(plan, profile))
            {
                availableProfiles.Add(profile);
                continue;
            }

            var logName = plan.InputArgument ?? profile.Resource.Channel;
            if (string.IsNullOrWhiteSpace(logName))
            {
                if (profile.Mandatory)
                {
                    throw new ArgumentException($"profile '{profile.Id}' requires resource.channel or a eventlog <logname> argument.");
                }

                // ValidateResources already emitted the optional-profile warning; keep this execution filter silent.
                continue;
            }

            if (WindowsEventLogInput.TryResolveLogName(logName, out _, out var errorMessage))
            {
                availableProfiles.Add(profile);
                continue;
            }

            if (WindowsEventLogInput.IsDisabledChannelError(errorMessage))
            {
                // ValidateResources already emitted the disabled-channel warning; keep this execution filter silent.
                continue;
            }

            if (profile.Mandatory)
            {
                throw new InvalidOperationException(errorMessage);
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
        if (profile.Condition is null)
        {
            return true;
        }

        if (!profile.Condition.Type.Equals("wmi", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

#if WINDOWS
        var scopePath = string.IsNullOrWhiteSpace(profile.Condition.ScopePath)
            ? @"\\.\root\cimv2"
            : profile.Condition.ScopePath;

        if (WmiCondition.TryExists(profile.Condition.Query, out var result, out var error, scopePath))
        {
            return result;
        }

        var message = $"profile '{profile.Id}' WMI condition could not be evaluated: {error?.Message ?? "unknown error"}";
        if (profile.Condition.Mandatory)
        {
            throw new InvalidOperationException(message, error);
        }

        Console.Error.WriteLine($"warning: {message}");
        Console.Error.Flush();
        return false;
#else
        var message = $"profile '{profile.Id}' WMI condition skipped because this build does not include Windows WMI support.";
        if (profile.Condition.Mandatory)
        {
            throw new PlatformNotSupportedException(message);
        }

        Console.Error.WriteLine($"warning: {message}");
        Console.Error.Flush();
        return false;
#endif
    }

    private static bool IsSchemaCommand(string value) => value is "schema" or "schemas" or "resources" or "list-schemas" or "list-resources";

    private static void ListSchemas(string[] args)
    {
        var path = "profiles";
        var format = "table";
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("json", StringComparison.OrdinalIgnoreCase) || args[i].Equals("table", StringComparison.OrdinalIgnoreCase))
            {
                format = args[i].ToLowerInvariant();
            }
            else if (args[i] is "--profiles" or "--resources" or "--path")
            {
                if (++i >= args.Length)
                {
                    throw new ArgumentException($"{args[i - 1]} requires a directory path.");
                }
                path = args[i];
            }
            else if (!args[i].StartsWith('-'))
            {
                path = args[i];
            }
            else
            {
                throw new ArgumentException($"unknown schema option '{args[i]}'");
            }
        }

        var schemas = CreateBuiltInSchemas();
        if (Directory.Exists(path))
        {
            var loader = new YamlResourceProfileLoader();
            var result = loader.LoadDirectory(path);
            if (result.Errors.Count > 0)
            {
                foreach (var error in result.Errors)
                {
                    Console.Error.WriteLine($"warning: {error}");
                    Console.Error.Flush();
                }
            }

            LogProfileLoadWarnings(result.Warnings);

            schemas.AddRange(result.Profiles.Select(profile => new ResourceSchemaDescription(
                    profile.Id,
                    profile.Name,
                    profile.Version,
                    profile.Enabled,
                    "profile",
                    profile.Resource.Platform,
                    profile.Resource.Family,
                    profile.Resource.Service,
                    profile.Resource.Channel,
                    profile.Resource.Provider,
                    profile.Input.Table,
                    profile.Input.Schema)));
        }

        schemas = schemas
            .OrderBy(schema => schema.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(schema => schema.Platform, StringComparer.OrdinalIgnoreCase)
            .ThenBy(schema => schema.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(schema => schema.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(schemas, NdjsonSerializerOptions.CreateDefault()));
            Console.Out.Flush();
            return;
        }

        Console.WriteLine("id\tsource\tplatform\tfamily\ttable\tschema");
        foreach (var schema in schemas)
        {
            Console.WriteLine($"{schema.Id}\t{schema.Source}\t{schema.Platform}\t{schema.Family}\t{schema.Table}\t{schema.Schema}");
        }
        Console.Out.Flush();
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
  dzagentctl [<input>] [<target>] [json [<file.ndjson>]] [--profile <profile.yaml|profiles-dir>]
  dzagentctl <input> [<arg>] [json [<file.ndjson>]] --kql <query> [--table <name>] [--schema <columns>]
  dzagentctl schemas [<profiles-dir>] [table|json]

Inputs:
  syslog <file>             Tail a local syslog-style file for new events.
  syslogserver [options]    Listen for syslog lines over TCP (default 0.0.0.0:514).
  fifo <path>               Create/read a Linux FIFO for syslog-style log lines.
  csv <file.csv>            Process a CSV file and then exit.
  auditd <file>             Process an auditd log file and then exit.
  eventlog <logname>        Listen for new Windows Event Log events (Windows build).
  evtx <file.evtx>          Process an EVTX file (Windows build).
  etl <file.etl>            Process an ETL trace file (Windows build).
  etw <session>             Listen to a real-time ETW session (Windows build).

Outputs:
  json [file.ndjson]        Write DeltaZulu NDJSON to stdout or append to a file (default).

Options:
  --profile <path>          Apply one profile, or every YAML profile under a directory.
                           When used, <input> may be omitted and inferred from the profile resource family.
  --kql, -q, --query        Apply inline KQL to the real-time input stream.
  --table <name>            KQL table name for --kql (default Source).
  --schema <columns>        Resource schema text to associate with --kql.
  --resource-id <id>        Resource id to stamp on --kql output metadata.
  --address <ip>            syslogserver bind address.
  --port <port>             syslogserver TCP port.

Examples:
  dzagentctl /var/log/auth.log --profile profiles/linux/syslog/sshd.yaml
  dzagentctl /var/log/auth.log json out.ndjson --profile profiles/linux/syslog
  dzagentctl csv events.csv json out.ndjson --kql "EventLog | where RawMessage has 'sudo'"
  dzagentctl eventlog --profile profiles/windows/eventlog
  dzagentctl eventlog sysmon --kql "EventLog | where EventId == 1"
  dzagentctl eventlog Security --kql "EventLog | where EventId == 4688"
  dzagentctl schemas profiles json
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
                if (!ShouldValidateWindowsEventLog(plan, profile))
                {
                    continue;
                }

                var validationResult = WindowsResourceValidator.ValidateEventLog(profile, plan.InputArgument);
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
            var logName = plan.InputArgument ?? throw new ArgumentException("eventlog requires <logname>");
            if (!WindowsEventLogInput.TryResolveLogName(logName, out _, out var errorMessage))
            {
                Console.Error.WriteLine($"error: {errorMessage}");
                Console.Error.Flush();
                return false;
            }
        }
#endif

        return true;
    }

#if WINDOWS
    private static bool ShouldValidateWindowsEventLog(CliPlan plan, ResourceProfile profile)
    {
        if (plan.InputCommand is not null)
        {
            return plan.InputCommand.Equals("eventlog", StringComparison.OrdinalIgnoreCase);
        }

        return ProfileFamilyToInputCommand(profile)?.Equals("eventlog", StringComparison.OrdinalIgnoreCase) == true;
    }
#endif
}