namespace DeltaZulu.Agent.Cli;

internal static class MetricsMode
{
    public static int Run(string[] args, ControllerModeContext context)
    {
        var sqlitePath = CliOptions.GetOption(args, "--sqlite")
            ?? CliOptions.GetOption(args, "--metrics-db")
            ?? "./state/dzagent-metrics.sqlite";
        var snapshot = SqliteMetricsStateProvider.Read(sqlitePath);
        var text = MetricsTextFormatter.Format(snapshot);

        if (context.UseTerminalGui)
        {
            TerminalGuiShell.TryRunMetricsDashboard(snapshot, context.Warn);
        }
        else
        {
            Console.WriteLine(text);
            Console.Out.Flush();
        }

        return 0;
    }
}
