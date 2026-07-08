using DeltaZulu.Agent.ProfileWorkbench;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Agent.Cli;

internal static class TailMode
{
    private const int DefaultLimit = 500;

    public static int Run(string[] args, ControllerModeContext context)
    {
        if (args.Length < 3)
        {
            throw new ArgumentException("tail mode requires a KQL query and a path: dzagentctl --tail \"<query>\" <path>");
        }

        if (!context.UseTerminalGui)
        {
            Console.Error.WriteLine("error: --tail requires an interactive terminal because it displays a live bounded result table.");
            Console.Error.Flush();
            return 1;
        }

        var query = args[1];
        var path = args[2];
        var input = CliOptions.GetOption(args, "--input");
        if (string.Equals(input, "auto", StringComparison.OrdinalIgnoreCase))
        {
            input = null;
        }

        var limitText = CliOptions.GetOption(args, "--limit");
        var limit = int.TryParse(limitText, out var parsedLimit) && parsedLimit > 0 ? parsedLimit : DefaultLimit;

        var registry = new WorkbenchSourceRegistry(context.Warn);
        var source = registry.BindTail(query, path, input, limit);
        var document = new ResourceProfileDocument(
            path,
            string.Empty,
            new ResourceProfile
            {
                SchemaVersion = 1,
                Id = $"cli.tail.{source.SourceKind}",
                Name = $"Tail {source.SourceKind}",
                Version = "1.0.0",
                Resource = new ResourceDescriptor { Platform = OperatingSystem.IsWindows() ? "windows" : "local", Family = source.SourceKind },
                Input = new ResourceInputContract { Table = source.Table, Schema = string.Join(',', source.Schema.Fields.Select(field => $"{field.Name}:{field.KqlType}")) },
                Filter = new ResourceFilter { Language = "kql", Query = query },
                Output = new ResourceOutputContract { Format = "table", PreserveOriginalFieldNames = true }
            });

        var request = new WorkbenchRunRequest(document, source, query, limit, WorkbenchRunMode.Follow);
        return TailTableTui.TryRun(request, new WorkbenchQueryRunner(), context.Warn) ? 0 : 1;
    }
}
