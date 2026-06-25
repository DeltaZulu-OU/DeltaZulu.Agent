using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Agent.Core.Pipelines;
using DeltaZulu.Agent.Inputs.Auditd;
using DeltaZulu.Agent.Inputs.Files;
using DeltaZulu.Agent.Inputs.Syslog;
using DeltaZulu.Agent.Kql;
using DeltaZulu.Agent.Outputs.Ndjson;
using DeltaZulu.Agent.Profiles;
using System.Net;
using System.Reactive.Linq;
using System.Text.Json;

#if WINDOWS
using DeltaZulu.Agent.Inputs.Windows;
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
            if (plan.IsProfileMode)
            {
                return RunProfiles(plan, sink, cts.Token);
            }

            using var executor = new ResourceKqlProfileExecutor();
            using var completed = new ManualResetEventSlim(false);
            using var doneSink = new CompletionTrackingSink(sink, completed);
            var trackedPipeline = new ResourcePipeline(
                CreateInput(plan),
                source => Execute(source, plan, executor, cts.Token),
                doneSink);

            using var subscription = trackedPipeline.Start(cts.Token);
            completed.Wait(cts.Token);
            if (doneSink.Error is not null)
            {
                LogPipelineError(doneSink.Error);
                return 1;
            }

            return 0;
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

    private static void LogPipelineError(Exception exception)
    {
        Console.Error.WriteLine("error: unhandled pipeline exception");
        Console.Error.WriteLine(exception);
        Console.Error.Flush();
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
            "Source",
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
            "Source",
            "RawMessage:string,ReceivedAt:datetime,SourceIpAddress:string,Priority:int,Facility:string,Severity:string,SyslogVersion:string,Timestamp:datetime,Hostname:string,AppName:string,ProcessName:string,ProcId:string,ProcessId:int,MsgId:string,StructuredData:string,Message:string,ExtractedData:dynamic,_metadata:dynamic"),
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
            "Source",
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
            "Source",
            "ID:string,RawEvent:dynamic,SYSCALL:dynamic,EXECVE:dynamic,PATH:dynamic,SOCKADDR:dynamic,CWD:dynamic,PROCTITLE:dynamic,_metadata:dynamic"),
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
            "Source",
            "ProviderName:string,EventId:int,Channel:string,RecordId:long,Level:string,Keywords:string,MachineName:string,TimeCreated:datetime,EventData:dynamic,Message:string,RawEvent:dynamic,_metadata:dynamic"),
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
            "Source",
            "ProviderName:string,EventName:string,EventId:int,OpcodeName:string,TaskName:string,Timestamp:datetime,ProcessId:int,ThreadId:int,Payload:dynamic,_metadata:dynamic")
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

    private static IResourceInput CreateInput(CliPlan plan) => CreateInput(plan, null);

    private static IResourceInput CreateInput(CliPlan plan, ResourceProfile? profile)
    {
        var inputCommand = plan.InputCommand ?? ProfileFamilyToInputCommand(profile)
            ?? throw new ArgumentException("input command is required when no profile resource family is available.");

        return inputCommand switch {
        "syslog" => new SyslogFileTailInput(plan.InputArgument ?? throw new ArgumentException("syslog profiles require a target <file> argument.")),
        "syslogserver" => new TcpSyslogInput(IPAddress.Parse(plan.Option("--address") ?? "0.0.0.0"), int.Parse(plan.Option("--port") ?? "514")),
        "csv" => new CsvFileInput(plan.InputArgument ?? throw new ArgumentException("csv profiles require a target <file.csv> argument.")),
        "auditd" => new AuditdFileInput(plan.InputArgument ?? throw new ArgumentException("auditd profiles require a target <file> argument.")),
#if WINDOWS
        "winlog" => new WindowsEventLogInput(plan.InputArgument ?? profile?.Resource.Channel ?? throw new ArgumentException("winlog profiles require resource.channel or a target <logname> argument.")),
        "evtx" => new EvtxFileInput(plan.InputArgument ?? throw new ArgumentException("evtx requires <file.evtx>")),
        "etl" => new EtlFileInput(plan.InputArgument ?? throw new ArgumentException("etl requires <file.etl>")),
        "etw" => new EtwSessionInput(plan.InputArgument ?? throw new ArgumentException("etw requires <session>")),
#else
        "winlog" or "evtx" or "etl" or "etw" => throw new PlatformNotSupportedException($"{inputCommand} is available from the net10.0-windows build."),
#endif
        _ => throw new ArgumentException($"unknown input command '{inputCommand}'")
        };
    }

    private static string? ProfileFamilyToInputCommand(ResourceProfile? profile) => profile?.Resource.Family.ToLowerInvariant() switch
    {
        "syslog" => "syslog",
        "syslogserver" => "syslogserver",
        "csv" => "csv",
        "auditd" => "auditd",
        "eventlog" => "winlog",
        "windows.eventlog" => "winlog",
        "evtx" => "evtx",
        "etl" => "etl",
        "etw" => "etw",
        _ => null
    };

    private static int RunProfiles(CliPlan plan, IResourceSink sink, CancellationToken cancellationToken)
    {
        var profiles = LoadProfiles(plan.Option("--profile")!);
        if (profiles.Count == 0)
        {
            Console.Error.WriteLine("error: no enabled profiles were found.");
            Console.Error.Flush();
            return 1;
        }

        using var channelSink = new ChannelResourceSink(sink);
        var tasks = profiles
            .Select(profile => Task.Run(() => RunProfile(plan, profile, channelSink, cancellationToken), cancellationToken))
            .ToArray();

        try
        {
            Task.WaitAll(tasks, cancellationToken);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
        {
            throw ex.InnerExceptions[0];
        }

        channelSink.Complete();
        if (channelSink.Error is not null)
        {
            LogPipelineError(channelSink.Error);
            return 1;
        }

        return 0;
    }

    private static void RunProfile(CliPlan plan, ResourceProfile profile, IResourceSink sink, CancellationToken cancellationToken)
    {
        using var executor = new ResourceKqlProfileExecutor();
        using var completed = new ManualResetEventSlim(false);
        using var doneSink = new CompletionTrackingSink(sink, completed, completeInner: false);
        var pipeline = new ResourcePipeline(
            CreateInput(plan, profile),
            source => executor.Execute(source, profile, cancellationToken),
            doneSink);

        using var subscription = pipeline.Start(cancellationToken);
        completed.Wait(cancellationToken);
        if (doneSink.Error is not null)
        {
            throw doneSink.Error;
        }
    }

    private static IReadOnlyList<ResourceProfile> LoadProfiles(string path)
    {
        var loader = new YamlResourceProfileLoader();
        if (Directory.Exists(path))
        {
            var result = loader.LoadDirectory(path);
            if (result.Errors.Count > 0)
            {
                throw new InvalidDataException(string.Join(Environment.NewLine, result.Errors));
            }

            return result.Profiles.Where(profile => profile.Enabled).ToList();
        }

        return [loader.LoadFile(path)];
    }

    private static IResourceSink CreateSink(CliPlan plan) => plan.OutputCommand switch {
        "json" => string.IsNullOrWhiteSpace(plan.OutputArgument) ? new ConsoleNdjsonSink() : new NdjsonFileSink(plan.OutputArgument),
        "table" => new ConsoleTableSink(),
        _ => throw new ArgumentException($"unknown output command '{plan.OutputCommand}'")
    };

    private static IObservable<ResourceOutputRecord> Execute(IObservable<SourceEvent> source, CliPlan plan, ResourceKqlProfileExecutor executor, CancellationToken cancellationToken)
    {
        var profilePath = plan.Option("--profile");
        var inlineKql = plan.Option("--kql") ?? plan.Option("--query") ?? plan.Option("-q");
        if (string.IsNullOrWhiteSpace(profilePath) && string.IsNullOrWhiteSpace(inlineKql))
        {
            return source.Select(sourceEvent => ResourceOutputRecord.FromSource(sourceEvent));
        }

        if (!string.IsNullOrWhiteSpace(profilePath) && !string.IsNullOrWhiteSpace(inlineKql))
        {
            throw new ArgumentException("Use either --profile <profile.yaml> or --kql <query>, not both.");
        }

        var profile = !string.IsNullOrWhiteSpace(profilePath)
            ? new YamlResourceProfileLoader().LoadFile(profilePath)
            : CreateInlineProfile(plan, inlineKql!);
        return executor.Execute(source, profile, cancellationToken);
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help";

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

    private static void PrintUsage()
    {
        Console.WriteLine("""
Usage:
  dzagent [<input>] [<target>] [<output> [<arg>]] [--profile <profile.yaml|profiles-dir>]
  dzagent <input> [<arg>] [<output> [<arg>]] --kql <query> [--table <name>] [--schema <columns>]
  dzagent schemas [<profiles-dir>] [table|json]

Inputs:
  syslog <file>             Tail a local syslog-style file for new events.
  syslogserver [options]    Listen for syslog lines over TCP (default 0.0.0.0:514).
  csv <file.csv>            Process a CSV file and then exit.
  auditd <file>             Process an auditd log file and then exit.
  winlog <logname>          Listen for new Windows Event Log events (Windows build).
  evtx <file.evtx>          Process an EVTX file (Windows build).
  etl <file.etl>            Process an ETL trace file (Windows build).
  etw <session>             Listen to a real-time ETW session (Windows build).

Outputs:
  json [file.ndjson]        Write DeltaZulu NDJSON to stdout or append to a file (default).
  table                    Print a compact console table.

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
  dzagent /var/log/auth.log table --profile profiles/linux/syslog/sshd.yaml
  dzagent /var/log/auth.log json out.ndjson --profile profiles/linux/syslog
  dzagent csv events.csv json out.ndjson --kql "Source | where RawMessage has 'sudo'"
  dzagent winlog table --profile profiles/windows/eventlog
  dzagent winlog sysmon --kql "Source | where EventId == 1"
  dzagent winlog Security --kql "Source | where EventId == 4688"
  dzagent schemas profiles json
""");
        Console.Out.Flush();
    }

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

                var logName = plan.InputArgument ?? profile.Resource.Channel;
                if (string.IsNullOrWhiteSpace(logName))
                {
                    Console.Error.WriteLine($"error: profile '{profile.Id}' requires resource.channel or a winlog <logname> argument.");
                    Console.Error.Flush();
                    return false;
                }

                if (!WindowsEventLogInput.TryResolveLogName(logName, out _, out var errorMessage))
                {
                    Console.Error.WriteLine($"error: {errorMessage}");
                    Console.Error.Flush();
                    return false;
                }
            }
        }
        else if (plan.InputCommand?.Equals("winlog", StringComparison.OrdinalIgnoreCase) == true)
        {
            var logName = plan.InputArgument ?? throw new ArgumentException("winlog requires <logname>");
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
            return plan.InputCommand.Equals("winlog", StringComparison.OrdinalIgnoreCase);
        }

        return ProfileFamilyToInputCommand(profile)?.Equals("winlog", StringComparison.OrdinalIgnoreCase) == true;
    }
#endif
}
