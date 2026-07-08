using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DeltaZulu.Agent.Cli;

internal static class MetricsDashboardTui
{
    public static bool TryRun(MetricsStateSnapshot snapshot, Action<string>? warn = null)
    {
        try
        {
            using var app = Application.Create().Init();
            using Window window = new() { Title = "DeltaZulu Agent Metrics" };
            var label = new Label
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                Text = MetricsTextFormatter.FormatDashboard(snapshot)
            };

            window.Add(label);
            app.Run(window);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            warn?.Invoke($"metrics TUI unavailable: {ex.GetBaseException().Message}");
            return false;
        }
    }
}
