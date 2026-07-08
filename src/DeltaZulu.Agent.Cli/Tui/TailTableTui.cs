using DeltaZulu.Agent.ProfileWorkbench;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DeltaZulu.Agent.Cli;

internal static class TailTableTui
{
    public static bool TryRun(WorkbenchRunRequest request, WorkbenchQueryRunner runner, Action<string>? warn = null)
    {
        try
        {
            using var cts = new CancellationTokenSource();
            using var app = Application.Create().Init();
            using Window window = new() { Title = "DeltaZulu Tail Query" };

            var query = new Label
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 3,
                Text = $"{request.Source.DisplayName}\n{request.Query}"
            };

            var status = new StatusBar
            {
                X = 0,
                Y = Pos.Bottom(query),
                Width = Dim.Fill(),
                Height = 1,
                Text = "following | Ctrl+Q/Esc close"
            };

            var tableModel = new BoundTableModel(request.RowLimit);
            tableModel.Reset(request.Source.Schema.Fields.Select(field => field.Name));
            var table = new TableView
            {
                X = 0,
                Y = Pos.Bottom(status),
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                Title = "Live Matching Rows",
                Table = new DataTableSource(tableModel.Table)
            };

            void UpdateStatus(WorkbenchCounters counters)
            {
                status.Text = $"read {counters.Read} | matched {counters.Matched} | shown {tableModel.Count} | errors {counters.Errors} | last {counters.LastEventUtc:O}";
                status.SetNeedsDraw();
            }

            var subscription = runner.RunLive(
                request,
                record => Application.Invoke(() => {
                    tableModel.Append(record.Event);
                    table.SetNeedsDraw();
                }),
                counters => Application.Invoke(() => UpdateStatus(counters)),
                ex => Application.Invoke(() => {
                    status.Text = $"tail error: {ex.GetBaseException().Message}";
                    status.SetNeedsDraw();
                }),
                cts.Token);

            window.Add(query, status, table);
            try
            {
                app.Run(window);
            }
            finally
            {
                cts.Cancel();
                subscription.Dispose();
            }

            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            warn?.Invoke($"tail TUI unavailable: {ex.GetBaseException().Message}");
            return false;
        }
    }
}
