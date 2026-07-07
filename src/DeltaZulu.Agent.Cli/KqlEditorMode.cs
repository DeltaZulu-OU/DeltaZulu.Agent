using System.Diagnostics;
using System.Reactive.Linq;
using DeltaZulu.Agent.Filter.Kql;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Agent.Cli;

internal static class KqlEditorMode
{
    public static int Run(string[] args, ControllerModeContext context)
    {
        var schemas = LoadLocalTuiSchemas(CliOptions.GetOption(args, "--profiles") ?? "profiles", context);

        Console.WriteLine("DeltaZulu KQL editor TUI");
        Console.WriteLine("Local resources are queryable as KQL tables. Commands: :schemas, :help, :quit");
        Console.WriteLine("Submit a query with a blank line, a trailing semicolon, or literal \\n separators.");
        Console.WriteLine($"Loaded schemas: {schemas.Count}");
        Console.Out.Flush();

        if (Console.IsInputRedirected)
        {
            var redirectedQuery = Console.In.ReadToEnd();
            return string.IsNullOrWhiteSpace(redirectedQuery) ? 0 : RunLocalKqlQuery(redirectedQuery, schemas);
        }

        if (context.UseTerminalGui)
        {
            TerminalGuiShell.TryRunKqlEditorIntro(schemas, context.Warn);
        }

        return RunEditorLoop(schemas);
    }

    private static int RunEditorLoop(IReadOnlyList<ResourceSchemaDescription> schemas)
    {
        var queryLines = new List<string>();
        while (true)
        {
            Console.Write(queryLines.Count == 0 ? "kql> " : "   > ");
            var line = Console.ReadLine();
            if (line is null)
            {
                return 0;
            }

            if (queryLines.Count == 0 && IsTuiCommand(line, out var command))
            {
                if (command is ":quit" or ":q")
                {
                    return 0;
                }

                if (command == ":schemas")
                {
                    PrintLocalSchemas(schemas);
                    continue;
                }

                if (command == ":help")
                {
                    PrintTuiHelp();
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                if (queryLines.Count > 0)
                {
                    _ = RunLocalKqlQuery(string.Join(Environment.NewLine, queryLines), schemas);
                    queryLines.Clear();
                }
                continue;
            }

            var submit = line.TrimEnd().EndsWith(';');
            queryLines.Add(submit ? line.TrimEnd().TrimEnd(';') : line);
            if (submit || line.Contains("\\n", StringComparison.Ordinal))
            {
                _ = RunLocalKqlQuery(string.Join(Environment.NewLine, queryLines), schemas);
                queryLines.Clear();
            }
        }
    }

    private static List<ResourceSchemaDescription> LoadLocalTuiSchemas(string profilesPath, ControllerModeContext context)
    {
        var schemas = new List<ResourceSchemaDescription>
        {
            new("local.processes", "Local process snapshot", context.Version, true, "built-in", OperatingSystem.IsWindows() ? "windows" : "local", "local", null, null, null, "Processes", "id:int,name:string,processName:string,workingSet:long,privateMemory:long,virtualMemory:long,threads:int,handleCount:int,startTime:datetime,path:string,commandLine:string"),
            new("local.runtime", "Current dzagentctl runtime", context.Version, true, "built-in", "local", "local", null, null, null, "Runtime", "processId:int,machineName:string,osVersion:string,frameworkDescription:string,workingSet:long,processorCount:int,commandLine:string"),
            new("local.environment", "Environment variables", context.Version, true, "built-in", "local", "local", null, null, null, "Environment", "name:string,value:string")
        };

        if (Directory.Exists(profilesPath))
        {
            var loader = new YamlResourceProfileLoader();
            var result = loader.LoadDirectory(profilesPath);
            context.LogProfileLoadWarnings(result.Warnings);
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

        return schemas;
    }

    private static bool IsTuiCommand(string line, out string command)
    {
        command = line.Trim().ToLowerInvariant();
        return command is ":schemas" or ":help" or ":quit" or ":q";
    }

    private static void PrintTuiHelp()
    {
        Console.WriteLine("Example:");
        Console.WriteLine("Processes");
        Console.WriteLine("| project name, memMB=workingSet/1024/1024");
        Console.WriteLine("| order by memMB desc");
        Console.WriteLine("| take 1");
        Console.WriteLine();
        Console.WriteLine("Use :schemas to list local resource tables and columns.");
    }

    private static void PrintLocalSchemas(IEnumerable<ResourceSchemaDescription> schemas)
    {
        Console.WriteLine("table\tid\tschema");
        foreach (var schema in schemas.OrderBy(schema => schema.Table, StringComparer.OrdinalIgnoreCase).ThenBy(schema => schema.Id, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{schema.Table}\t{schema.Id}\t{schema.Schema}");
        }
    }

    private static int RunLocalKqlQuery(string query, IReadOnlyList<ResourceSchemaDescription> schemas)
    {
        query = query.Replace("\\n", Environment.NewLine, StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        var table = ResolveLocalTable(query, schemas);
        if (table is null)
        {
            Console.Error.WriteLine("error: query must start with a known local table. Run :schemas to see available resources.");
            Console.Error.Flush();
            return 1;
        }

        if (!IsBuiltInLocalTable(table))
        {
            Console.Error.WriteLine($"error: table '{table}' has a schema but no local TUI snapshot provider. Use dzagentctl --tail for file-backed resources.");
            Console.Error.Flush();
            return 1;
        }

        var events = CreateLocalResourceSnapshot(table);
        var profile = new ResourceProfile
        {
            SchemaVersion = 1,
            Id = $"cli.tui.{table.ToLowerInvariant()}",
            Name = $"Local {table} query",
            Version = "1.0.0",
            Resource = new ResourceDescriptor { Platform = "local", Family = "local" },
            Input = new ResourceInputContract
            {
                Table = table,
                Schema = schemas.FirstOrDefault(schema => schema.Table.Equals(table, StringComparison.OrdinalIgnoreCase))?.Schema ?? string.Empty
            },
            Filter = new ResourceFilter { Language = "kql", Query = query },
            Output = new ResourceOutputContract { Format = "table", PreserveOriginalFieldNames = true }
        };

        using var executor = new ResourceKqlProfileExecutor();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        Exception? error = null;
        using var completed = new ManualResetEventSlim();
        using var subscription = executor.Execute(events.ToObservable(), profile).Subscribe(
            record => rows.Add(record.Event),
            ex => { error = ex; completed.Set(); },
            () => completed.Set());

        if (!completed.Wait(TimeSpan.FromSeconds(10)))
        {
            Console.Error.WriteLine("error: query did not complete within 10 seconds.");
            Console.Error.Flush();
            return 1;
        }

        if (error is not null)
        {
            Console.Error.WriteLine($"error: {error.Message}");
            Console.Error.Flush();
            return 1;
        }

        PrintRows(rows);
        return 0;
    }

    private static bool IsBuiltInLocalTable(string table) => table.Equals("Processes", StringComparison.OrdinalIgnoreCase)
        || table.Equals("Runtime", StringComparison.OrdinalIgnoreCase)
        || table.Equals("Environment", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveLocalTable(string query, IEnumerable<ResourceSchemaDescription> schemas)
    {
        var firstToken = query.Split([' ', '\t', '\r', '\n', '|'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstToken is null)
        {
            return null;
        }

        return schemas.Select(schema => schema.Table).Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(table => table.Equals(firstToken, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<SourceEvent> CreateLocalResourceSnapshot(string table) => table.ToLowerInvariant() switch
    {
        "processes" => CreateProcessRows(),
        "runtime" =>
        [
            new SourceEvent(CreateLocalMetadata("Runtime"), new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["processId"] = Environment.ProcessId,
                ["machineName"] = Environment.MachineName,
                ["osVersion"] = Environment.OSVersion.ToString(),
                ["frameworkDescription"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                ["workingSet"] = Environment.WorkingSet,
                ["processorCount"] = Environment.ProcessorCount,
                ["commandLine"] = Environment.CommandLine
            })
        ],
        "environment" => Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Select(entry => new SourceEvent(CreateLocalMetadata("Environment"), new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = entry.Key?.ToString() ?? string.Empty,
                ["value"] = entry.Value?.ToString() ?? string.Empty
            }))
            .ToArray(),
        _ => []
    };

    private static SourceEvent[] CreateProcessRows() => Process.GetProcesses()
        .Select(process =>
        {
            using (process)
            {
                return new SourceEvent(CreateLocalMetadata("Processes"), new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = process.Id,
                    ["name"] = process.ProcessName,
                    ["processName"] = process.ProcessName,
                    ["workingSet"] = SafeRead(() => process.WorkingSet64),
                    ["privateMemory"] = SafeRead(() => process.PrivateMemorySize64),
                    ["virtualMemory"] = SafeRead(() => process.VirtualMemorySize64),
                    ["threads"] = SafeRead(() => process.Threads.Count),
                    ["handleCount"] = SafeRead(() => process.HandleCount),
                    ["startTime"] = SafeRead<DateTime?>(() => process.StartTime),
                    ["path"] = SafeRead(() => process.MainModule?.FileName),
                    ["commandLine"] = process.Id == Environment.ProcessId ? Environment.CommandLine : null
                });
            }
        })
        .ToArray();

    private static T? SafeRead<T>(Func<T> read)
    {
        try { return read(); }
        catch { return default; }
    }

    private static ResourceMetadata CreateLocalMetadata(string table) => new()
    {
        SourceType = "local",
        SourceName = table,
        Platform = OperatingSystem.IsWindows() ? "windows" : "local",
        ParserName = "dzagentctl.local",
        RawPreserved = false
    };

    private static void PrintRows(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        if (rows.Count == 0)
        {
            Console.WriteLine("(0 rows)");
            return;
        }

        var columns = rows.SelectMany(row => row.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Console.WriteLine(string.Join('\t', columns));
        foreach (var row in rows)
        {
            Console.WriteLine(string.Join('\t', columns.Select(column => FormatCell(row.TryGetValue(column, out var value) ? value : null))));
        }
    }

    private static string FormatCell(object? value) => value switch
    {
        null => string.Empty,
        DateTime dateTime => dateTime.ToString("O"),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O"),
        _ => value.ToString() ?? string.Empty
    };
}
