using System.Net;
using DeltaZulu.Agent.Runtime;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Pipeline.Inputs.Relp;
using DeltaZulu.Pipeline.Outputs.Ndjson;

namespace DeltaZulu.Demo.Collector;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var address = IPAddress.Parse(GetOption(args, "--address") ?? "127.0.0.1");
        var port = int.Parse(GetOption(args, "--port") ?? "6514", System.Globalization.CultureInfo.InvariantCulture);
        var prettyPrint = HasFlag(args, "-p") || HasFlag(args, "--pretty");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        using var executor = new DemoCollectorPassthroughExecutor();
        using var sink = new ConsoleNdjsonSink(prettyPrint: prettyPrint);
        var runtime = new AgentRuntime(
            [new ProfileBinding(
                new RelpInput(new RelpInputConfiguration
                {
                    Address = address.ToString(),
                    Port = port,
                    UseTls = false
                }, "demo-relp-collector"),
                CreatePassThroughProfile(),
                executor)],
            sink,
            warn: message => Console.Error.WriteLine($"warning: {message}"));

        Console.Error.WriteLine($"DeltaZulu demo collector pipeline listening for MessagePack RELP on {address}:{port}");
        try
        {
            var result = runtime.Run(cts.Token);
            if (!result.Success)
            {
                Console.Error.WriteLine(result.Error);
                return 1;
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
    }

    private static ResourceProfile CreatePassThroughProfile() => new() {
        Id = "demo-relp-collector.passthrough",
        Name = "Demo RELP collector passthrough",
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

    private static bool HasFlag(string[] args, string flag)
    {
        foreach (var token in args)
        {
            if (token.Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetOption(string[] args, string option)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (token.StartsWith(option + "=", StringComparison.OrdinalIgnoreCase))
            {
                return token[(option.Length + 1)..];
            }

            if (token.Equals(option, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"{option} requires a value.");
                }

                return args[index + 1];
            }
        }

        return null;
    }

    private static void PrintUsage() => Console.WriteLine("""
Usage:
  dzdemo-collector [--address <ip>] [--port <port>] [-p|--pretty]

Runs the standard DeltaZulu pipeline with a MessagePack RELP input,
a pass-through profile with no KQL filter, and console NDJSON output.

Options:
  -p, --pretty           Pretty-print console JSON output for readability.
""");
}

internal sealed class DemoCollectorPassthroughExecutor : IProfileExecutor
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