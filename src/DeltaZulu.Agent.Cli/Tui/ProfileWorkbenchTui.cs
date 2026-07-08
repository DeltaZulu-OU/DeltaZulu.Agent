using System.Data;
using DeltaZulu.Agent.ProfileWorkbench;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DeltaZulu.Agent.Cli;

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
            using Window window = new() { Title = "DeltaZulu Profile Query Workbench" };

            var status = new StatusBar
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 1,
                Text = "Profile workbench | Run tests daemon profile filters locally | Save is explicit"
            };

            var profileTree = new TreeView
            {
                X = 0,
                Y = Pos.Bottom(status),
                Width = Dim.Percent(30),
                Height = Dim.Fill(),
                Title = "Profile Library"
            };
            profileTree.AddObjects(BuildProfileTree(profiles));
            profileTree.ExpandAll();

            var sourceLabel = new Label
            {
                X = Pos.Right(profileTree) + 1,
                Y = Pos.Bottom(status),
                Width = 14,
                Height = 1,
                Text = "Source:"
            };

            var sourceText = new TextField
            {
                X = Pos.Right(sourceLabel) + 1,
                Y = Pos.Bottom(status),
                Width = Dim.Fill(),
                Height = 1,
                Text = initialSource ?? string.Empty
            };

            var previousButton = new Button
            {
                X = Pos.Right(profileTree) + 1,
                Y = Pos.Bottom(sourceText) + 1,
                Text = "_Previous",
                AssignHotKeys = true
            };

            var nextButton = new Button
            {
                X = Pos.Right(previousButton) + 1,
                Y = Pos.Bottom(sourceText) + 1,
                Text = "_Next",
                AssignHotKeys = true
            };

            var runButton = new Button
            {
                X = Pos.Right(nextButton) + 1,
                Y = Pos.Bottom(sourceText) + 1,
                Text = "_Run",
                AssignHotKeys = true
            };

            var saveButton = new Button
            {
                X = Pos.Right(runButton) + 1,
                Y = Pos.Bottom(sourceText) + 1,
                Text = "_Save",
                AssignHotKeys = true
            };

            var queryEditor = new TextView
            {
                X = Pos.Right(profileTree) + 1,
                Y = Pos.Bottom(runButton) + 1,
                Width = Dim.Fill(),
                Height = Dim.Percent(42),
                Title = "Profile KQL"
            };

            var results = new TableView
            {
                X = Pos.Right(profileTree) + 1,
                Y = Pos.Bottom(queryEditor) + 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                Title = "Matched Rows"
            };

            var tableModel = new BoundTableModel(ResultLimit);
            tableModel.Reset(["Result"]);
            results.Table = new DataTableSource(tableModel.Table);

            var index = 0;
            var initialSourceBinding = NormalizeOptionalText(initialSource);
            ResourceProfileDocument current = library.Open(profiles[index]);

            void LoadCurrentProfile()
            {
                current = library.Open(profiles[index]);
                queryEditor.Text = current.Query;
                var candidate = sourceRegistry.CandidateFromProfile(current.Profile);
                sourceText.Text = initialSourceBinding ?? candidate.PathOrResource ?? string.Empty;
                var binding = candidate.RequiresBinding ? "source required" : $"source {candidate.PathOrResource}";
                status.Text = $"Profile {index + 1}/{profiles.Count}: {current.Profile.Id} | {candidate.DisplayName} | {binding} | schema fields {candidate.Schema.Fields.Count}";
                tableModel.Reset(["Result"]);
                results.SetNeedsDraw();
                status.SetNeedsDraw();
            }

            void RunQuery()
            {
                try
                {
                    current.Query = queryEditor.Text?.ToString() ?? string.Empty;
                    var source = sourceRegistry.Bind(current.Profile, NormalizeOptionalText(sourceText.Text?.ToString()), WorkbenchRunMode.Replay);
                    var request = new WorkbenchRunRequest(current, source, current.Query, ResultLimit, WorkbenchRunMode.Replay);
                    var result = runner.RunOnce(request, TimeSpan.FromSeconds(10));
                    tableModel.SetRows(result.Rows);
                    var suffix = result.Truncated ? " | truncated" : string.Empty;
                    status.Text = result.Error is null
                        ? $"Read {result.Counters.Read} | matched {result.Counters.Matched} | displayed {result.Counters.Displayed}{suffix}"
                        : $"Query failed: {result.Error}";
                    results.SetNeedsDraw();
                    status.SetNeedsDraw();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    status.Text = $"Run failed: {ex.GetBaseException().Message}";
                    status.SetNeedsDraw();
                }
            }

            void SaveProfile()
            {
                current.Query = queryEditor.Text?.ToString() ?? string.Empty;
                var save = library.Save(current);
                status.Text = save.Success ? $"Saved {save.Path}" : $"Save failed: {save.Error}";
                status.SetNeedsDraw();
            }

            previousButton.Accepting += (_, _) => {
                index = index == 0 ? profiles.Count - 1 : index - 1;
                LoadCurrentProfile();
            };
            nextButton.Accepting += (_, _) => {
                index = (index + 1) % profiles.Count;
                LoadCurrentProfile();
            };
            runButton.Accepting += (_, _) => RunQuery();
            saveButton.Accepting += (_, _) => SaveProfile();

            window.Add(status, profileTree, sourceLabel, sourceText, previousButton, nextButton, runButton, saveButton, queryEditor, results);
            LoadCurrentProfile();
            app.Run(window);
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

    private static IEnumerable<ITreeNode> BuildProfileTree(IEnumerable<ProfileLibraryItem> profiles)
    {
        var root = new ProfileNode("Profiles");
        foreach (var platformGroup in profiles.GroupBy(p => string.IsNullOrWhiteSpace(p.Platform) ? "unknown" : p.Platform, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var platform = root.Add(platformGroup.Key);
            foreach (var familyGroup in platformGroup.GroupBy(p => string.IsNullOrWhiteSpace(p.Family) ? "unknown" : p.Family, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var family = platform.Add(familyGroup.Key);
                foreach (var profile in familyGroup.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
                {
                    family.Add($"{profile.Id} [{profile.Table}]");
                }
            }
        }

        return [root];
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
