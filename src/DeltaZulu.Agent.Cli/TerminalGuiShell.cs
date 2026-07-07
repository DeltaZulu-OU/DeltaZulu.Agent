using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DeltaZulu.Agent.Cli;

internal static class TerminalGuiShell
{
    public static bool TryRunKqlEditorIntro(IReadOnlyList<ResourceSchemaDescription> schemas, Action<string>? warn = null) =>
        TryRunInfoShell("DeltaZulu KQL Editor (Esc to continue)", BuildKqlIntroText(schemas), warn);

    public static bool TryRunMetricsView(Action<string>? warn = null) =>
        TryRunInfoShell("DeltaZulu Agent Metrics (Esc to close)", BuildMetricsText(), warn);

    public static bool TryRunMetricsDashboard(MetricsStateSnapshot snapshot, Action<string>? warn = null) =>
        TryRunInfoShell("DeltaZulu Agent Metrics Dashboard (Esc to close)", MetricsTextFormatter.FormatDashboard(snapshot), warn);

    public static bool TryRunTailView(string query, string path, string table, string input, Action<string>? warn = null) =>
        TryRunInfoShell("DeltaZulu KQL Tail (Esc to start tail)", BuildTailText(query, path, table, input), warn);

    public static bool TryRunInfoShell(string title, string body, Action<string>? warn = null)
    {
        try
        {
            using IApplication app = Application.Create().Init();

            using Window window = new() { Title = title };
            Label label = new()
            {
                Text = body,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            window.Add(label);

            app.Run(window);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            warn?.Invoke($"Terminal.Gui shell unavailable; falling back to console output: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private static string BuildKqlIntroText(IReadOnlyList<ResourceSchemaDescription> schemas)
    {
        var localSchemas = schemas
            .Where(schema => schema.Source.Equals("built-in", StringComparison.OrdinalIgnoreCase))
            .OrderBy(schema => schema.Table, StringComparer.OrdinalIgnoreCase)
            .Select(schema => $"  {schema.Table}: {schema.Schema}");

        return $"""
DeltaZulu local KQL editor

Queryable local resources:
{string.Join(Environment.NewLine, localSchemas)}

Example:
  Processes\n| project name, memMB=workingSet/1024/1024\n| order by memMB desc\n| take 1

Press Esc to continue to the KQL command editor.
Use :schemas in the editor to list all loaded resource schemas.
""";
    }

    private static string BuildMetricsText() => $"""
DeltaZulu agent metrics

Agent-only monitoring views:
  pipeline throughput
  input lag
  dropped records
  backpressure
  RELP output
  profile health

This view is scoped to dzagentd telemetry, not host CPU/memory/process monitoring.
Press Esc to close.
""";

    private static string BuildTailText(string query, string path, string table, string input) => $"""
DeltaZulu KQL tail

Path:  {path}
Input: {input}
Table: {table}

Query:
{query}

Press Esc to start streaming matching records.
""";
}
