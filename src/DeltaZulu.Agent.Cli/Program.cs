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

internal static class Program
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
            return 0;
        }

        try
        {
            var plan = CliPlan.Parse(args);
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            using var executor = new ResourceKqlProfileExecutor();
            using var sink = CreateSink(plan);
            using var completed = new ManualResetEventSlim(false);
            using var doneSink = new CompletionTrackingSink(sink, completed);
            var trackedPipeline = new ResourcePipeline(
                CreateInput(plan),
                source => Execute(source, plan, executor, cts.Token),
                doneSink);

            using var subscription = trackedPipeline.Start(cts.Token);
            completed.Wait(cts.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static IResourceInput CreateInput(CliPlan plan) => plan.InputCommand switch {
        "syslog" => new SyslogFileTailInput(plan.InputArgument ?? throw new ArgumentException("syslog requires <file>")),
        "syslogserver" => new TcpSyslogInput(IPAddress.Parse(plan.Option("--address") ?? "0.0.0.0"), int.Parse(plan.Option("--port") ?? "514")),
        "csv" => new CsvFileInput(plan.InputArgument ?? throw new ArgumentException("csv requires <file.csv>")),
        "auditd" => new AuditdFileInput(plan.InputArgument ?? throw new ArgumentException("auditd requires <file>")),
#if WINDOWS
        "winlog" => new WindowsEventLogInput(plan.InputArgument ?? throw new ArgumentException("winlog requires <logname>")),
        "evtx" => new EvtxFileInput(plan.InputArgument ?? throw new ArgumentException("evtx requires <file.evtx>")),
        "etl" => new EtlFileInput(plan.InputArgument ?? throw new ArgumentException("etl requires <file.etl>")),
        "etw" => new EtwSessionInput(plan.InputArgument ?? throw new ArgumentException("etw requires <session>")),
#else
        "winlog" or "evtx" or "etl" or "etw" => throw new PlatformNotSupportedException($"{plan.InputCommand} is available from the net10.0-windows build."),
#endif
        _ => throw new ArgumentException($"unknown input command '{plan.InputCommand}'")
    };

    private static IResourceSink CreateSink(CliPlan plan) => plan.OutputCommand switch {
        "json" => string.IsNullOrWhiteSpace(plan.OutputArgument) ? new ConsoleNdjsonSink() : new NdjsonFileSink(plan.OutputArgument),
        "table" => new ConsoleTableSink(),
        _ => throw new ArgumentException($"unknown output command '{plan.OutputCommand}'")
    };

    private static IObservable<ResourceOutputRecord> Execute(IObservable<SourceEvent> source, CliPlan plan, ResourceKqlProfileExecutor executor, CancellationToken cancellationToken)
    {
        var profilePath = plan.Option("--profile") ?? plan.Option("--query") ?? plan.Option("-q");
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            return source.Select(sourceEvent => ResourceOutputRecord.FromSource(sourceEvent));
        }

        var profile = new YamlResourceProfileLoader().LoadFile(profilePath);
        return executor.Execute(source, profile, cancellationToken);
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help";

    private static void PrintUsage() => Console.WriteLine("""
Usage: dzagent <input> [<arg>] [<output> [<arg>]] [--profile <profile.yaml>]

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
  --profile, -q, --query    Apply a DeltaZulu YAML resource profile containing KQL.
  --address <ip>            syslogserver bind address.
  --port <port>             syslogserver TCP port.

Examples:
  dzagent syslog /var/log/auth.log table --profile profiles/linux/syslog/sshd.yaml
  dzagent csv events.csv json out.ndjson --profile profiles/linux/syslog/pam.yaml
""");

    private sealed class CompletionTrackingSink : IResourceSink
    {
        private readonly ManualResetEventSlim _completed;
        private readonly IResourceSink _inner;

        public CompletionTrackingSink(IResourceSink inner, ManualResetEventSlim completed)
        {
            (_inner, _completed) = (inner, completed);
        }

        public string Name => _inner.Name;

        public void Dispose() => _inner.Dispose();

        public void OnCompleted()
        { _inner.OnCompleted(); _completed.Set(); }

        public void OnError(Exception error)
        { _inner.OnError(error); _completed.Set(); }

        public void OnNext(ResourceOutputRecord value) => _inner.OnNext(value);
    }

    private sealed class ConsoleTableSink : IResourceSink
    {
        private readonly JsonSerializerOptions _jsonOptions = NdjsonSerializerOptions.CreateDefault();
        private bool _printedHeader;
        public string Name => "table-console";

        public void Dispose()
        { }

        public void OnCompleted()
        { }

        public void OnError(Exception error) => Console.Error.WriteLine(error.Message);

        public void OnNext(ResourceOutputRecord value)
        {
            if (!_printedHeader)
            {
                Console.WriteLine("timestamp\tsource\tevent");
                _printedHeader = true;
            }

            value.Metadata.TryGetValue("ingestedAt", out var timestamp);
            value.Metadata.TryGetValue("sourceName", out var source);
            Console.WriteLine($"{timestamp}\t{source}\t{JsonSerializer.Serialize(value.Event, _jsonOptions)}");
        }
    }

    private sealed record CliPlan(string InputCommand, string? InputArgument, string OutputCommand, string? OutputArgument, IReadOnlyDictionary<string, string?> Options)
    {
        public string? Option(string name) => Options.TryGetValue(name, out var value) ? value : null;

        public static CliPlan Parse(string[] args)
        {
            var input = args[0].ToLowerInvariant();
            string? inputArg = null;
            var index = 1;
            if (index < args.Length && !args[index].StartsWith('-') && !IsOutput(args[index]))
            {
                inputArg = args[index++];
            }

            var output = "json";
            string? outputArg = null;
            if (index < args.Length && IsOutput(args[index]))
            {
                output = args[index++].ToLowerInvariant();
                if (index < args.Length && !args[index].StartsWith('-'))
                {
                    outputArg = args[index++];
                }
            }

            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            while (index < args.Length)
            {
                var key = args[index++];
                if (!key.StartsWith('-'))
                {
                    throw new ArgumentException($"unexpected argument '{key}'");
                }

                string? value = null;
                var equals = key.IndexOf('=');
                if (equals >= 0)
                {
                    value = key[(equals + 1)..];
                    key = key[..equals];
                }
                else if (index < args.Length && !args[index].StartsWith('-'))
                {
                    value = args[index++];
                }
                options[key] = value;
            }

            return new CliPlan(input, inputArg, output, outputArg, options);
        }

        private static bool IsOutput(string value) => value.Equals("json", StringComparison.OrdinalIgnoreCase) || value.Equals("table", StringComparison.OrdinalIgnoreCase);
    }
}