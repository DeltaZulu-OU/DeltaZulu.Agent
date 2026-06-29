namespace DeltaZulu.Agent.Cli;

internal sealed record CliPlan(string? InputCommand, string? InputArgument, string? OutputPath, IReadOnlyDictionary<string, string?> Options)
{
    public bool IsProfileMode => !string.IsNullOrWhiteSpace(Option("--profile"));

    public string? Option(string name) => Options.TryGetValue(name, out var value) ? value : null;

    public static CliPlan Parse(string[] args)
    {
        string? input = null;
        string? inputArg = null;
        string? outputPath = null;
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (token.StartsWith('-'))
            {
                var (key, value, consumedNext) = ParseOption(args, index);
                options[key] = value;
                if (consumedNext)
                {
                    index++;
                }
                continue;
            }

            if (IsOutput(token))
            {
                if (index + 1 < args.Length && !args[index + 1].StartsWith('-') && !IsInput(args[index + 1]) && !IsOutput(args[index + 1]))
                {
                    outputPath = args[++index];
                }
                continue;
            }

            if (token.Equals("table", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("table output has been removed; omit the output argument for console NDJSON or use 'json <file.ndjson>' to write NDJSON to a file.");
            }

            if (input is null)
            {
                if (IsInput(token))
                {
                    input = token.ToLowerInvariant();
                    if (index + 1 < args.Length && !args[index + 1].StartsWith('-') && !IsOutput(args[index + 1]) && !args[index + 1].Equals("table", StringComparison.OrdinalIgnoreCase))
                    {
                        inputArg = args[++index];
                    }
                }
                else if (inputArg is null)
                {
                    inputArg = token;
                }
                else
                {
                    throw new ArgumentException($"unexpected argument '{token}'");
                }
                continue;
            }

            throw new ArgumentException($"unexpected argument '{token}'");
        }

        if (string.IsNullOrWhiteSpace(input) && !options.ContainsKey("--profile"))
        {
            throw new ArgumentException("input command is required unless --profile is used. Put an input such as 'eventlog Security' before or after --kql.");
        }

        return new CliPlan(input, inputArg, outputPath, options);
    }

    private static bool IsOutput(string value) => value.Equals("json", StringComparison.OrdinalIgnoreCase);

    internal static bool IsInput(string value) => value.Equals("syslog", StringComparison.OrdinalIgnoreCase)
        || value.Equals("syslogserver", StringComparison.OrdinalIgnoreCase)
        || value.Equals("fifo", StringComparison.OrdinalIgnoreCase)
        || value.Equals("csv", StringComparison.OrdinalIgnoreCase)
        || value.Equals("auditd", StringComparison.OrdinalIgnoreCase)
        || value.Equals("eventlog", StringComparison.OrdinalIgnoreCase)
        || value.Equals("evtx", StringComparison.OrdinalIgnoreCase)
        || value.Equals("etl", StringComparison.OrdinalIgnoreCase)
        || value.Equals("etw", StringComparison.OrdinalIgnoreCase);

    private static (string Key, string? Value, bool ConsumedNext) ParseOption(string[] args, int index)
    {
        var key = args[index];
        var equals = key.IndexOf('=');
        if (equals >= 0)
        {
            return (key[..equals], key[(equals + 1)..], false);
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
        {
            throw new ArgumentException($"{key} requires a value.");
        }

        return (key, args[index + 1], true);
    }
}

internal sealed record ResourceSchemaDescription(
    string Id,
    string Name,
    string Version,
    bool Enabled,
    string Source,
    string Platform,
    string Family,
    string? Service,
    string? Channel,
    string? Provider,
    string Table,
    string Schema);