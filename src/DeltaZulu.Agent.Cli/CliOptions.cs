namespace DeltaZulu.Agent.Cli;

internal static class CliOptions
{
    public static string? GetOption(IReadOnlyList<string> args, string option)
    {
        var prefix = option + "=";
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[prefix.Length..];
            }

            if (arg.Equals(option, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Count && !args[i + 1].StartsWith('-')
                    ? args[i + 1]
                    : null;
            }
        }

        return null;
    }
}
