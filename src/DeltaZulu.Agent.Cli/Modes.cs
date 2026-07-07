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
            WarnStderr,
            LogProfileLoadWarnings,
            RunPipelinePlan);

        return args[0] switch
        {
            "--tui" or "tui" => KqlEditorMode.Run(args, context),
            "--metrics" or "metrics" => MetricsMode.Run(args, context),
            "--tail" or "tail" => TailMode.Run(args, context),
            _ => throw new ArgumentException($"unknown mode '{args[0]}'")
        };
    }

    private static bool ShouldUseTerminalGui(IEnumerable<string> args) => !Console.IsInputRedirected
        && !args.Any(arg => arg.Equals(NoTerminalGuiOption, StringComparison.OrdinalIgnoreCase));

    private static bool IsServiceCommand(string value) => value is "start" or "stop" or "restart" or "status" or "reload";

    private static int RunServiceCommand(string[] args) => ServiceControlCommand.Run(args);
}

internal sealed record ControllerModeContext(
    string Version,
    bool UseTerminalGui,
    Action<string> Warn,
    Action<IEnumerable<string>> LogProfileLoadWarnings,
    Func<string[], int> RunPipelinePlan);
