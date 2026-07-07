namespace DeltaZulu.Agent.Cli;

internal static class CliOptions
{
    public static string? GetOption(IReadOnlyList<string> args, string option)
    {
        var prefix = option + "=";
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return args[i][prefix.Length..];
            }

            if (args[i].Equals(option, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
