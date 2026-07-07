using System.Data;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DeltaZulu.Agent.Cli;

internal static class TerminalGuiShell
{
    public static bool TryRunKqlEditorWorkspace(
        IReadOnlyList<ResourceSchemaDescription> schemas,
        Func<string, LocalKqlQueryResult> runQuery,
        Action<string>? warn = null)
    {
        try
        {
            using var app = Application.Create().Init();

            using Window window = new() { Title = "DeltaZulu KQL Editor" };
            StatusBar statusBar = new()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 1,
                Text = "Run button: execute query   Esc: close   Tab: move focus"
            };

            TreeView schemaTree = new()
            {
                X = 0,
                Y = Pos.Bottom(statusBar),
                Width = Dim.Percent(28),
                Height = Dim.Fill(),
                Title = "Schemas"
            };
            schemaTree.AddObjects(BuildSchemaTree(schemas));
            schemaTree.ExpandAll();

            Button runButton = new()
            {
                X = Pos.Right(schemaTree) + 1,
                Y = Pos.Bottom(statusBar),
                Text = "_Run Query",
                AssignHotKeys = true
            };

            Code queryEditor = new()
            {
                X = Pos.Right(schemaTree) + 1,
                Y = Pos.Bottom(runButton) + 1,
                Width = Dim.Fill(),
                Height = Dim.Percent(45),
                Title = "Query Editor",
                Text = BuildDefaultQuery(schemas),
                Language = "KQL"
            };

            TableView results = new()
            {
                X = Pos.Right(schemaTree) + 1,
                Y = Pos.Bottom(queryEditor) + 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                Title = "Results"
            };

            void ExecuteQuery()
            {
                var result = runQuery(queryEditor.Text?.ToString() ?? string.Empty);
                results.Table = new DataTableSource(BuildResultsTable(result));
                statusBar.Text = BuildStatusText(result);
                results.SetNeedsDraw();
                statusBar.SetNeedsDraw();
            }

            runButton.Accepting += (_, _) => ExecuteQuery();
            ExecuteQuery();

            window.Add(statusBar, schemaTree, runButton, queryEditor, results);
            app.Run(window);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            warn?.Invoke($"Terminal.Gui shell unavailable: {ex.GetBaseException().Message}");
            return false;
        }
    }

    public static bool TryRunMetricsDashboard(MetricsStateSnapshot snapshot, Action<string>? warn = null) =>
        TryRunInfoShell("DeltaZulu Agent Metrics Dashboard (Esc to close)", MetricsTextFormatter.FormatDashboard(snapshot), warn);

    private static bool TryRunInfoShell(string title, string body, Action<string>? warn = null)
    {
        try
        {
            using var app = Application.Create().Init();

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
            warn?.Invoke($"Terminal.Gui shell unavailable: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private static string BuildStatusText(LocalKqlQueryResult result)
    {
        if (result.Error is not null)
        {
            return $"Query failed: {result.Error}";
        }

        var suffix = result.Truncated ? " (truncated)" : string.Empty;
        return $"Query returned {result.Rows.Count} row(s){suffix}. Run button: execute query   Esc: close";
    }

    private static IEnumerable<ITreeNode> BuildSchemaTree(IReadOnlyList<ResourceSchemaDescription> schemas)
    {
        var root = new SchemaTreeNode("Schemas");
        foreach (var sourceGroup in schemas
            .OrderBy(schema => schema.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(schema => schema.Table, StringComparer.OrdinalIgnoreCase)
            .ThenBy(schema => schema.Id, StringComparer.OrdinalIgnoreCase)
            .GroupBy(schema => string.IsNullOrWhiteSpace(schema.Source) ? "resources" : schema.Source, StringComparer.OrdinalIgnoreCase))
        {
            var sourceNode = root.Add(sourceGroup.Key);
            foreach (var tableGroup in sourceGroup.GroupBy(schema => schema.Table, StringComparer.OrdinalIgnoreCase))
            {
                var tableNode = sourceNode.Add(tableGroup.Key);
                foreach (var column in SplitSchemaColumns(tableGroup.First().Schema))
                {
                    tableNode.Add(column);
                }
            }
        }

        return [root];
    }

    private static string BuildDefaultQuery(IReadOnlyList<ResourceSchemaDescription> schemas)
    {
        var defaultTable = schemas.FirstOrDefault(schema => schema.Table.Equals("Processes", StringComparison.OrdinalIgnoreCase))?.Table
            ?? schemas.OrderBy(schema => schema.Table, StringComparer.OrdinalIgnoreCase).FirstOrDefault()?.Table
            ?? "Processes";

        return $"""
{defaultTable}
""";
    }

    private static DataTable BuildResultsTable(LocalKqlQueryResult result)
    {
        var table = new DataTable("QueryResults");
        if (result.Error is not null)
        {
            table.Columns.Add("Error", typeof(string));
            table.Rows.Add(result.Error);
            return table;
        }

        if (result.Rows.Count == 0)
        {
            table.Columns.Add("Result", typeof(string));
            table.Rows.Add("0 rows");
            return table;
        }

        var columns = result.Rows.SelectMany(row => row.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var column in columns)
        {
            table.Columns.Add(column, typeof(string));
        }

        foreach (var row in result.Rows)
        {
            table.Rows.Add(columns.Select(column => FormatCell(row.TryGetValue(column, out var value) ? value : null)).ToArray());
        }

        return table;
    }

    private static string FormatCell(object? value) => value switch
    {
        null => string.Empty,
        DateTime dateTime => dateTime.ToString("O"),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O"),
        _ => value.ToString() ?? string.Empty
    };

    private static IEnumerable<string> SplitSchemaColumns(string schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            yield break;
        }

        foreach (var column in schema.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return column;
        }
    }

    private sealed class SchemaTreeNode(string text) : ITreeNode
    {
        public IList<ITreeNode> Children { get; } = new List<ITreeNode>();

        public object? Tag { get; set; }

        public string Text { get; set; } = text;

        public SchemaTreeNode Add(string childText)
        {
            var child = new SchemaTreeNode(childText);
            Children.Add(child);
            return child;
        }
    }
}
