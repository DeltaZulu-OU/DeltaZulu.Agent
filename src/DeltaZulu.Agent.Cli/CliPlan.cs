#if WINDOWS
#endif

namespace DeltaZulu.Agent.Cli;

internal static partial class Program
{
    private sealed record CliPlan(string InputCommand, string? InputArgument, string OutputCommand, string? OutputArgument, IReadOnlyDictionary<string, string?> Options)
    {
        public string? Option(string name) => Options.TryGetValue(name, out var value) ? value : null;

        public static CliPlan Parse(string[] args)
        {
            string? input = null;
            string? inputArg = null;
            var output = "json";
            string? outputArg = null;
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
                    output = token.ToLowerInvariant();
                    if (index + 1 < args.Length && !args[index + 1].StartsWith('-') && !IsInput(args[index + 1]) && !IsOutput(args[index + 1]))
                    {
                        outputArg = args[++index];
                    }
                    continue;
                }

                if (input is null)
                {
                    input = token.ToLowerInvariant();
                    if (index + 1 < args.Length && !args[index + 1].StartsWith('-') && !IsOutput(args[index + 1]))
                    {
                        inputArg = args[++index];
                    }
                    continue;
                }

                throw new ArgumentException($"unexpected argument '{token}'");
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException("input command is required. Put an input such as 'winlog Security' before or after --kql.");
            }

            return new CliPlan(input, inputArg, output, outputArg, options);
        }

        private static bool IsOutput(string value) => value.Equals("json", StringComparison.OrdinalIgnoreCase) || value.Equals("table", StringComparison.OrdinalIgnoreCase);

        private static bool IsInput(string value) => value.Equals("syslog", StringComparison.OrdinalIgnoreCase)
            || value.Equals("syslogserver", StringComparison.OrdinalIgnoreCase)
            || value.Equals("csv", StringComparison.OrdinalIgnoreCase)
            || value.Equals("auditd", StringComparison.OrdinalIgnoreCase)
            || value.Equals("winlog", StringComparison.OrdinalIgnoreCase)
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
}
