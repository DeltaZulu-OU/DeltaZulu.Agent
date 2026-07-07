using System.Diagnostics;
using System.Reactive.Linq;
using DeltaZulu.Agent.Filter.Kql;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Agent.Cli;

internal sealed record LocalKqlQueryResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    string? Error,
    bool Truncated = false);

internal static class KqlEditorMode
{
    private const int MaxLocalResultRows = 1_000;

    public static int Run(string[] args, ControllerModeContext context)
    {
        var schemas = LoadLocalTuiSchemas(CliOptions.GetOption(args, "--profiles") ?? "profiles", context);
        var oneShotQuery = CliOptions.GetOption(args, "--query")
            ?? CliOptions.GetOption(args, "--kql")
            ?? CliOptions.GetOption(args, "-q");

        if (!string.IsNullOrWhiteSpace(oneShotQuery))
        {
            return RunLocalKqlQuery(oneShotQuery, schemas);
        }

        if (Console.IsInputRedirected)
        {
            var redirectedQuery = Console.In.ReadToEnd();
            return string.IsNullOrWhiteSpace(redirectedQuery) ? 0 : RunLocalKqlQuery(redirectedQuery, schemas);
        }

        if (!context.UseTerminalGui)
        {
            Console.Error.WriteLine("error: --tui requires Terminal.Gui. Pipe a query on stdin or pass --query for non-interactive execution.");
            Console.Error.Flush();
            return 1;
        }

        return TerminalGuiShell.TryRunKqlEditorWorkspace(schemas, query => ExecuteLocalKqlQuery(query, schemas), context.Warn)
            ? 0
            : 1;
    }

    private static List<ResourceSchemaDescription> LoadLocalTuiSchemas(string profilesPath, ControllerModeContext context)
    {
        var schemas = new List<ResourceSchemaDescription>
        {
            new("local.processes", "Local process snapshot", context.Version, true, "built-in", OperatingSystem.IsWindows() ? "windows" : "local", "local", null, null, null, "Processes", "id:int,name:string,processName:string,workingSet:long,privateMemory:long,virtualMemory:long,threads:int,handleCount:int,startTime:datetime,path:string,commandLine:string"),
            new("local.runtime", "Current dzagentctl runtime", context.Version, true, "built-in", "local", "local", null, null, null, "Runtime", "processId:int,machineName:string,osVersion:string,frameworkDescription:string,workingSet:long,processorCount:int,commandLine:string"),
            new("local.environment", "Environment variables", context.Version, true, "built-in", "local", "local", null, null, null, "Environment", "name:string,value:string")
        };

        if (!Directory.Exists(profilesPath))
        {
            return schemas;
        }

        var loader = new YamlResourceProfileLoader();
        var result = loader.LoadDirectory(profilesPath);
        context.LogProfileLoadWarnings(result.Warnings);
        if (result.Errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, result.Errors));
        }

        schemas.AddRange(result.Profiles
            .Where(profile => profile.Enabled)
            .Select(profile => new ResourceSchemaDescription(
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

        return schemas;
    }

    private static int RunLocalKqlQuery(string query, IReadOnlyList<ResourceSchemaDescription> schemas)
    {
        var result = ExecuteLocalKqlQuery(query, schemas);
        if (result.Error is not null)
        {
            Console.Error.WriteLine($"error: {result.Error}");
            Console.Error.Flush();
            return 1;
        }

        PrintRows(result);
        return 0;
    }

    internal static LocalKqlQueryResult ExecuteLocalKqlQuery(string query, IReadOnlyList<ResourceSchemaDescription> schemas)
    {
        query = query.Replace("\\n", Environment.NewLine, StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return new LocalKqlQueryResult([], null);
        }

        var table = ResolveLocalTable(query, schemas);
        if (table is null)
        {
            return new LocalKqlQueryResult([], "query must start with a known local table.");
        }

        if (!IsBuiltInLocalTable(table))
        {
            return new LocalKqlQueryResult([], $"table '{table}' has a schema but no local snapshot provider. Use dzagentctl --tail for file-backed resources.");
        }

        var profile = CreateLocalProfile(table, query, schemas);
        var rows = new List<IReadOnlyDictionary<string, object?>>(capacity: Math.Min(MaxLocalResultRows, 128));
        var truncated = false;
        Exception? error = null;

        using var executor = new ResourceKqlProfileExecutor();
        using var completed = new ManualResetEventSlim();
        using var subscription = executor.Execute(CreateLocalResourceSnapshot(table).ToObservable(), profile)
            .Take(MaxLocalResultRows + 1)
            .Subscribe(
                record =>
                {
                    if (rows.Count < MaxLocalResultRows)
                    {
                        rows.Add(record.Event);
                        return;
                    }

                    truncated = true;
                },
                ex => { error = ex; completed.Set(); },
                () => completed.Set());

        if (!completed.Wait(TimeSpan.FromSeconds(10)))
        {
            return new LocalKqlQueryResult([], "query did not complete within 10 seconds.");
        }

        return error is null
            ? new LocalKqlQueryResult(rows, null, truncated)
            : new LocalKqlQueryResult([], error.Message);
    }

    private static ResourceProfile CreateLocalProfile(string table, string query, IReadOnlyList<ResourceSchemaDescription> schemas) => new()
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

    private static IEnumerable<SourceEvent> CreateLocalResourceSnapshot(string table)
    {
        return table.ToLowerInvariant() switch
        {
            "processes" => CreateProcessRows(),
            "runtime" => CreateRuntimeRows(),
            "environment" => CreateEnvironmentRows(),
            _ => []
        };
    }

    private static IEnumerable<SourceEvent> CreateRuntimeRows()
    {
        yield return new SourceEvent(CreateLocalMetadata("Runtime"), new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["processId"] = Environment.ProcessId,
            ["machineName"] = Environment.MachineName,
            ["osVersion"] = Environment.OSVersion.ToString(),
            ["frameworkDescription"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["workingSet"] = Environment.WorkingSet,
            ["processorCount"] = Environment.ProcessorCount,
            ["commandLine"] = Environment.CommandLine
        });
    }

    private static IEnumerable<SourceEvent> CreateEnvironmentRows()
    {
        var metadata = CreateLocalMetadata("Environment");
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            yield return new SourceEvent(metadata, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = entry.Key?.ToString() ?? string.Empty,
                ["value"] = entry.Value?.ToString() ?? string.Empty
            });
        }
    }

    private static IEnumerable<SourceEvent> CreateProcessRows()
    {
        var metadata = CreateLocalMetadata("Processes");
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                yield return new SourceEvent(metadata, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
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
        }
    }

    private static T? SafeRead<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return default;
        }
    }

    private static ResourceMetadata CreateLocalMetadata(string table) => new()
    {
        SourceType = "local",
        SourceName = table,
        Platform = OperatingSystem.IsWindows() ? "windows" : "local",
        ParserName = "dzagentctl.local",
        RawPreserved = false
    };

    private static void PrintRows(LocalKqlQueryResult result)
    {
        if (result.Rows.Count == 0)
        {
            Console.WriteLine("(0 rows)");
            return;
        }

        var columns = result.Rows.SelectMany(row => row.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Console.WriteLine(string.Join('\t', columns));
        foreach (var row in result.Rows)
        {
            Console.WriteLine(string.Join('\t', columns.Select(column => FormatCell(row.TryGetValue(column, out var value) ? value : null))));
        }

        if (result.Truncated)
        {
            Console.Error.WriteLine($"warning: result set truncated to {MaxLocalResultRows:N0} rows.");
            Console.Error.Flush();
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
