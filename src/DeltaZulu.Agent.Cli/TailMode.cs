using System.Reactive.Linq;
using DeltaZulu.Agent.Filter.Kql;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Pipeline.Outputs.Ndjson;

namespace DeltaZulu.Agent.Cli;

internal static class TailMode
{
    public static int Run(string[] args, ControllerModeContext context)
    {
        if (args.Length < 3)
        {
            throw new ArgumentException("tail mode requires a KQL query and a path: dzagentctl --tail \"<query>\" <path>");
        }

        var query = args[1];
        var path = args[2];
        var plan = CreateTailPlan(path);

        if (context.UseTerminalGui)
        {
            TerminalGuiShell.TryRunTailView(query, path, plan.Table, plan.Input, context.Warn);
        }

        if (plan.UsesPipelineInput)
        {
            var forwarded = new[] { plan.Input, path, "--kql", query, "--table", plan.Table };
            return context.RunPipelinePlan(forwarded);
        }

        return RunLineFallback(query, path, plan.Table);
    }

    private static TailPlan CreateTailPlan(string path)
    {
        var input = InferInputCommand(path);
        return input switch
        {
            "csv" => new TailPlan("csv", "Csv", true),
            "auditd" => new TailPlan("auditd", "Auditd", true),
            "syslog" => new TailPlan("syslog", "Syslog", true),
            _ => new TailPlan("line", "Lines", false)
        };
    }

    private static int RunLineFallback(string query, string path, string table)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"error: tail path does not exist: {path}");
            Console.Error.Flush();
            return 1;
        }

        var profile = new ResourceProfile
        {
            SchemaVersion = 1,
            Id = "cli.tail.lines",
            Name = "Line tail fallback",
            Version = "1.0.0",
            Resource = new ResourceDescriptor { Platform = "local", Family = "line" },
            Input = new ResourceInputContract { Table = table, Schema = "line:string" },
            Filter = new ResourceFilter { Language = "kql", Query = query },
            Output = new ResourceOutputContract { Format = "ndjson", PreserveOriginalFieldNames = true }
        };

        using var executor = new ResourceKqlProfileExecutor();
        using var sink = new ConsoleNdjsonSink();
        Exception? error = null;
        using var completed = new ManualResetEventSlim();
        using var subscription = executor.Execute(ReadLineEvents(path).ToObservable(), profile).Subscribe(
            sink.OnNext,
            ex => { error = ex; completed.Set(); },
            () => completed.Set());

        completed.Wait();
        if (error is null)
        {
            return 0;
        }

        sink.OnError(error);
        return 1;
    }

    private static IEnumerable<SourceEvent> ReadLineEvents(string path)
    {
        var lineNumber = 0L;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            yield return new SourceEvent(
                new ResourceMetadata
                {
                    SourceType = "file",
                    SourceName = path,
                    Platform = "local",
                    ParserName = "dzagentctl.line",
                    RawPreserved = true,
                    Properties = new Dictionary<string, object?> { ["lineNumber"] = lineNumber }
                },
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["line"] = line });
        }
    }

    private static string? InferInputCommand(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".csv")
        {
            return "csv";
        }

        var fileName = Path.GetFileName(path).ToLowerInvariant();
        if (fileName.Contains("audit", StringComparison.OrdinalIgnoreCase))
        {
            return "auditd";
        }

        return IsKnownSyslogFile(fileName) ? "syslog" : null;
    }

    private static bool IsKnownSyslogFile(string fileName) => fileName is "syslog" or "messages" or "secure"
        || fileName.EndsWith(".syslog", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith("auth.log", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith("kern.log", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith("daemon.log", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith("mail.log", StringComparison.OrdinalIgnoreCase);

    private sealed record TailPlan(string Input, string Table, bool UsesPipelineInput);
}
