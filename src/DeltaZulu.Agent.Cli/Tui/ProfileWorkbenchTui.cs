using System.Data;
using DeltaZulu.Agent.ProfileWorkbench;
using DeltaZulu.Pipeline.Core.Events;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Terminal.Gui.Editor;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Input;
using Application = Terminal.Gui.App.Application;

namespace DeltaZulu.Agent.Cli.Tui;

internal static class ProfileWorkbenchTui
{
    private const int ResultLimit = 1_000;

    public static bool TryRun(
        ProfileLibrary library,
        IReadOnlyList<ProfileLibraryItem> profiles,
        WorkbenchSourceRegistry sourceRegistry,
        WorkbenchQueryRunner runner,
        string? initialSource,
        Action<string>? warn = null)
    {
        try
        {
            using var app = Application.Create().Init();
            using Window window = new() { Title = "DeltaZulu Source Query Workbench" };

            var status = new StatusBar {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 1,
                Text = "Source workbench | live query stream | select sources or fields to insert KQL"
            };

            var profileTree = new TreeView {
                X = 0,
                Y = Pos.Bottom(status),
                Width = Dim.Percent(30),
                Height = Dim.Fill(),
                Title = "Schema"
            };
            profileTree.AddObjects(BuildSchemaTree(library, profiles));
            profileTree.ExpandAll();

            var sourceLabel = new Label {
                X = Pos.Right(profileTree) + 1,
                Y = Pos.Bottom(status),
                Width = 14,
                Height = 1,
                Text = "Source:"
            };

            var sourceText = new TextField {
                X = Pos.Right(sourceLabel) + 1,
                Y = Pos.Bottom(status),
                Width = Dim.Fill(),
                Height = 1,
                Text = initialSource ?? string.Empty
            };

            var tableLabel = new Label {
                X = Pos.Right(profileTree) + 1,
                Y = Pos.Bottom(sourceText) + 1,
                Width = Dim.Fill(),
                Height = 1,
                Text = "Table: - | Columns: 0"
            };

            var previousButton = new Button {
                X = Pos.Right(profileTree) + 1,
                Y = Pos.Bottom(tableLabel) + 1,
                Text = "_Previous",
                AssignHotKeys = true
            };

            var nextButton = new Button {
                X = Pos.Right(previousButton) + 1,
                Y = Pos.Bottom(tableLabel) + 1,
                Text = "_Next",
                AssignHotKeys = true
            };

            var runButton = new Button {
                X = Pos.Right(nextButton) + 1,
                Y = Pos.Bottom(tableLabel) + 1,
                Text = "_Follow",
                AssignHotKeys = true
            };

            var clearButton = new Button {
                X = Pos.Right(runButton) + 1,
                Y = Pos.Bottom(tableLabel) + 1,
                Text = "_Clear",
                AssignHotKeys = true
            };

            var saveButton = new Button {
                X = Pos.Right(clearButton) + 1,
                Y = Pos.Bottom(tableLabel) + 1,
                Text = "_Save",
                AssignHotKeys = true
            };

            var queryEditor = new Editor {
                X = Pos.Right(profileTree) + 1,
                Y = Pos.Bottom(runButton) + 1,
                Width = Dim.Fill(),
                Height = Dim.Percent(42),
                Title = "KQL",
                ConvertTabsToSpaces = true,
                IndentationSize = 4,
                GutterOptions = GutterOptions.LineNumbers,
                ViewportSettings = ViewportSettingsFlags.HasScrollBars
            };

            var results = new TableView {
                X = Pos.Right(profileTree) + 1,
                Y = Pos.Bottom(queryEditor) + 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                Title = "Live Matched Rows"
            };

            var tableModel = new BoundTableModel(ResultLimit);
            tableModel.Reset(["Result"]);
            results.Table = new DataTableSource(tableModel.Table);

            var index = 0;
            var initialSourceBinding = NormalizeOptionalText(initialSource);
            var current = library.Open(profiles[index]);
            IDisposable? liveSubscription = null;
            CancellationTokenSource? liveCts = null;
            WorkbenchCounters lastCounters = new(0, 0, 0, 0, null);

            void PostUi(Action action)
            {
                try
                {
                    app.Invoke(action);
                }
                catch (ObjectDisposedException)
                {
                    // Late observable callbacks can arrive while the TUI is closing.
                }
                catch (InvalidOperationException)
                {
                    // Terminal.Gui rejects dispatch after the instance application has stopped.
                }
            }

            void StopLive(string? message = null)
            {
                liveCts?.Cancel();
                liveSubscription?.Dispose();
                liveCts?.Dispose();
                liveSubscription = null;
                liveCts = null;
                runButton.Text = "_Follow";

                if (!string.IsNullOrWhiteSpace(message))
                {
                    status.Text = message;
                    status.SetNeedsDraw();
                }

                runButton.SetNeedsDraw();
            }

            void SetSourceTextFromProfile()
            {
                if (!string.IsNullOrWhiteSpace(initialSource))
                {
                    return;
                }

                var candidate = sourceRegistry.CandidateFromProfile(current.Profile);
                sourceText.Text = candidate.PathOrResource ?? string.Empty;
                sourceText.SetNeedsDraw();
            }

            void LoadCurrentProfile()
            {
                StopLive();
                current = library.Open(profiles[index]);
                queryEditor.Document = new TextDocument(current.Query);
                SetSourceTextFromProfile();
                var candidate = sourceRegistry.CandidateFromProfile(current.Profile);
                sourceText.Text = initialSourceBinding ?? candidate.PathOrResource ?? string.Empty;
                var binding = candidate.RequiresBinding ? "source required" : $"source {candidate.PathOrResource}";
                tableLabel.Text = $"Table: {candidate.Schema.Table} | Columns: {candidate.Schema.Fields.Count}";
                tableLabel.SetNeedsDraw();
                status.Text = $"Profile {index + 1}/{profiles.Count}: {current.Profile.Id} | {candidate.DisplayName} | {binding} | schema fields {candidate.Schema.Fields.Count}";
                tableModel.Reset(candidate.Schema.Fields.Count > 0 ? candidate.Schema.Fields.Select(field => field.Name) : ["Result"]);
                results.SetNeedsDraw();
                status.SetNeedsDraw();
            }

            void UpdateStatus(WorkbenchCounters counters)
            {
                lastCounters = counters;
                status.Text = $"following | read {counters.Read} | matched {counters.Matched} | shown {tableModel.Count} | errors {counters.Errors} | last {FormatTimestamp(counters.LastEventUtc)}";
                status.SetNeedsDraw();
            }

            void StartLive()
            {
                if (liveSubscription is not null)
                {
                    StopLive($"stopped | read {lastCounters.Read} | matched {lastCounters.Matched} | shown {tableModel.Count} | errors {lastCounters.Errors}");
                    return;
                }

                try
                {
                    current.Query = queryEditor.Document?.Text ?? string.Empty;
                    var source = sourceRegistry.Bind(current.Profile, sourceText.Text?.ToString(), WorkbenchRunMode.Follow);
                    var request = new WorkbenchRunRequest(current, source, current.Query, ResultLimit, WorkbenchRunMode.Follow);
                    liveCts = new CancellationTokenSource();
                    lastCounters = new WorkbenchCounters(0, 0, 0, 0, null);
                    tableModel.Reset(source.Schema.Fields.Count > 0 ? source.Schema.Fields.Select(field => field.Name) : ["Result"]);
                    results.SetNeedsDraw();

                    liveSubscription = runner.RunLive(
                    request,
                    record => PostUi(() => {
                        tableModel.Append(record.Event);
                        var sourceFields = source.Schema.Fields.Select(field => field.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var derivedFields = record.Metadata.TryGetValue(ResourceOutputRecord.QueryDerivedFieldsMetadataKey, out var value) && value is IEnumerable<string> fields
                            ? fields.ToArray()
                            : record.Event.Keys.Where(field => !sourceFields.Contains(field)).ToArray();
                        var shape = record.Metadata.TryGetValue(ResourceOutputRecord.QueryResultShapeMetadataKey, out var resultShape)
                            ? resultShape?.ToString()
                            : "unknown";
                        tableLabel.Text = derivedFields.Length == 0
                            ? $"Table: {source.Schema.Table} | Result: {shape} | all displayed fields are source fields"
                            : $"Table: {source.Schema.Table} | Result: {shape} | query-derived: {string.Join(", ", derivedFields)}";
                        tableLabel.SetNeedsDraw();
                        results.SetNeedsDraw();
                    }),
                    counters => PostUi(() => UpdateStatus(counters)),
                    ex => PostUi(() => StopLive($"live query error: {ex.GetBaseException().Message}")),
                    liveCts.Token,
                    () => PostUi(() => StopLive($"source completed | read {lastCounters.Read} | matched {lastCounters.Matched} | shown {tableModel.Count} | errors {lastCounters.Errors}")));

                    runButton.Text = "_Stop";
                    runButton.SetNeedsDraw();
                    status.Text = $"following {source.DisplayName} | waiting for matching rows";
                    status.SetNeedsDraw();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    StopLive();
                    status.Text = $"follow failed: {ex.GetBaseException().Message}";
                    status.SetNeedsDraw();
                }
            }

            void ClearResults()
            {
                tableModel.Clear();
                results.SetNeedsDraw();
                status.Text = liveSubscription is null
                                    ? "results cleared"
                                    : $"following | read {lastCounters.Read} | matched {lastCounters.Matched} | shown 0 | errors {lastCounters.Errors} | last {FormatTimestamp(lastCounters.LastEventUtc)}";
                status.SetNeedsDraw();
            }

            void SaveProfile()
            {
                current.Query = queryEditor.Document?.Text ?? string.Empty;
                var save = library.Save(current);
                status.Text = save.Success ? $"Saved {save.Path}" : $"Save failed: {save.Error}";
                status.SetNeedsDraw();
            }

            bool TrySelectProfile(WorkbenchSchemaTreeNode node)
            {
                if (string.IsNullOrWhiteSpace(node.ProfileId))
                {
                    return false;
                }

                for (var candidateIndex = 0; candidateIndex < profiles.Count; candidateIndex++)
                {
                    if (!profiles[candidateIndex].Id.Equals(node.ProfileId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (candidateIndex == index)
                    {
                        return true;
                    }

                    index = candidateIndex;
                    LoadCurrentProfile();
                    return true;
                }

                status.Text = $"profile '{node.ProfileId}' is not available in this workbench session";
                status.SetNeedsDraw();
                return false;
            }

            void InsertSchemaSelection(WorkbenchSchemaTreeNode node)
            {
                TrySelectProfile(node);

                var insertion = WorkbenchSchemaTree.InsertionText(node);
                if (string.IsNullOrWhiteSpace(insertion))
                {
                    return;
                }

                var currentText = queryEditor.Document?.Text ?? string.Empty;
                var separator = string.IsNullOrEmpty(currentText) || currentText.EndsWith('\n') || currentText.EndsWith(' ')
                    ? string.Empty
                    : Environment.NewLine;
                queryEditor.Document = new TextDocument(currentText + separator + insertion);
                status.Text = $"selected {current.Profile.Id} | inserted {node.Text}";
                queryEditor.SetFocus();
                queryEditor.SetNeedsDraw();
                status.SetNeedsDraw();
            }

            profileTree.Activated += (_, args) => {
                if (args.Value?.Value is ProfileNode { Tag: WorkbenchSchemaTreeNode schemaNode })
                {
                    InsertSchemaSelection(schemaNode);
                }
            };


            void WireButton(Button button, Action action)
            {
                //button.MouseBindings.Add(MouseFlags.LeftButtonClicked, Command.Accept);
                button.Accepting += (_, args) => {
                    args.Handled = true;
                    action();
                };
            }
            WireButton(previousButton, () => {
                index = index == 0 ? profiles.Count - 1 : index - 1;
                LoadCurrentProfile();
            });
            WireButton(nextButton, () => {
                index = (index + 1) % profiles.Count;
                LoadCurrentProfile();
            });
            WireButton(runButton, StartLive);
            WireButton(saveButton, SaveProfile);
            WireButton(clearButton, ClearResults);

            window.Add(status, profileTree, sourceLabel, sourceText, tableLabel, previousButton, nextButton, runButton, clearButton, saveButton, queryEditor, results);
            LoadCurrentProfile();
            try
            {
                app.Run(window);
            }
            finally
            {
                StopLive();
            }
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            warn?.Invoke($"profile workbench TUI unavailable: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatTimestamp(DateTimeOffset? value) => value?.ToString("O") ?? "-";

    private static IEnumerable<ITreeNode> BuildSchemaTree(ProfileLibrary library, IEnumerable<ProfileLibraryItem> profiles)
    {
        var documents = profiles.Select(profile => library.Open(profile).Profile);
        return [ToTreeNode(WorkbenchSchemaTree.Build(documents))];
    }

    private static ProfileNode ToTreeNode(WorkbenchSchemaTreeNode schemaNode)
    {
        var node = new ProfileNode(schemaNode.Text) { Tag = schemaNode };
        foreach (var child in schemaNode.Children)
        {
            node.Children.Add(ToTreeNode(child));
        }

        return node;
    }

    private sealed class ProfileNode(string text) : ITreeNode
    {
        public IList<ITreeNode> Children { get; } = new List<ITreeNode>();
        public object? Tag { get; set; }
        public string Text { get; set; } = text;

        public ProfileNode Add(string text)
        {
            var child = new ProfileNode(text);
            Children.Add(child);
            return child;
        }
    }
}
