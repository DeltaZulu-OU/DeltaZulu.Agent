namespace DeltaZulu.Agent.Cli;

internal static partial class Program
{
    private const string NoTerminalGuiOption = "--no-terminal-gui";

    private static bool IsModeCommand(string value) => value is "--tui" or "tui" or "--metrics" or "metrics" or "--tail" or "tail";

    private static int RunModeCommand(string[] args)
    {
        var context = new ControllerModeContext(
            Version,
            ShouldUseTerminalGui(args),
            WarnStderr);

        return args[0] switch
        {
            "--tui" or "tui" => ProfileWorkbenchMode.Run(args, context),
            "--metrics" or "metrics" => MetricsMode.Run(args, context),
            "--tail" or "tail" => TailMode.Run(args, context),
            _ => throw new ArgumentException($"unknown mode '{args[0]}'")
        };
    }

    private static bool ShouldUseTerminalGui(IEnumerable<string> args)
    {
        var argv = args as string[] ?? args.ToArray();
        if (Console.IsInputRedirected || argv.Any(arg => arg.Equals(NoTerminalGuiOption, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return argv.Length > 0 && argv[0] is "--tui" or "tui" or "--metrics" or "metrics" or "--tail" or "tail";
    }

    private static bool IsServiceCommand(string value) => value is "start" or "stop" or "restart" or "status" or "reload";

    private static int RunServiceCommand(string[] args) => ServiceControlCommand.Run(args);

    private static void WarnStderr(string message)
    {
        Console.Error.WriteLine($"warning: {message}");
        Console.Error.Flush();
    }
}

internal sealed record ControllerModeContext(
    string Version,
    bool UseTerminalGui,
    Action<string> Warn);
