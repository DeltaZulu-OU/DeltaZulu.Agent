namespace DeltaZulu.Agent.Cli;

internal static class MetricsMode
{
    public static int Run(string[] args, ControllerModeContext context)
    {
        var sqlitePath = CliOptions.GetOption(args, "--sqlite")
            ?? CliOptions.GetOption(args, "--metrics-db")
            ?? "./state/dzagent-metrics.sqlite";

        var snapshot = SqliteMetricsStateProvider.Read(sqlitePath);
        if (context.UseTerminalGui && MetricsDashboardTui.TryRun(snapshot, context.Warn))
        {
            return 0;
        }

        Console.WriteLine(MetricsTextFormatter.Format(snapshot));
        Console.Out.Flush();
        return 0;
    }
}
