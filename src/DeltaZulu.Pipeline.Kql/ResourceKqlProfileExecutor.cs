using System.Reactive.Disposables;
using System.Reactive.Kql;
using System.Reactive.Kql.CustomTypes;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Pipeline.Kql;

/// <summary>
/// Executes resource profile KQL over SourceEvent rows.
/// This class intentionally keeps KQL as the profile language while preserving source-native fields in NDJSON output.
/// </summary>
public sealed class ResourceKqlProfileExecutor : IProfileExecutor
{
    private static readonly Lazy<bool> ScalarFunctionsRegistered = new(RegisterScalarFunctions, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Lock _gate = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly List<string> _temporaryQueryFiles = [];
    private int _disposed;

    /// <summary>
    /// Normalizes Kusto syntax aliases that are not accepted by Microsoft.Rx.Kql.
    /// </summary>
    public static string NormalizeQueryForRxKql(string query) => NormalizeQueryForRxKql(query, inputTableName: null, observableName: null);

    public static string NormalizeQueryForRxKql(string query, string? inputTableName, string? observableName)
    {
        ArgumentNullException.ThrowIfNull(query);

        const string notInAlias = "notin";
        var normalized = new StringBuilder(query.Length);
        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;

        for (var i = 0; i < query.Length; i++)
        {
            var current = query[i];

            if (current == '\'' && !inDoubleQuotedString)
            {
                normalized.Append(current);
                if (inSingleQuotedString && i + 1 < query.Length && query[i + 1] == '\'')
                {
                    normalized.Append(query[++i]);
                    continue;
                }

                inSingleQuotedString = !inSingleQuotedString;
                continue;
            }

            if (current == '"' && !inSingleQuotedString)
            {
                normalized.Append(current);
                if (inDoubleQuotedString && i + 1 < query.Length && query[i + 1] == '"')
                {
                    normalized.Append(query[++i]);
                    continue;
                }

                inDoubleQuotedString = !inDoubleQuotedString;
                continue;
            }

            if (!inSingleQuotedString && !inDoubleQuotedString)
            {
                if (!string.IsNullOrWhiteSpace(inputTableName)
                    && !string.IsNullOrWhiteSpace(observableName)
                    && !inputTableName.Equals(observableName, StringComparison.OrdinalIgnoreCase)
                    && IsKeywordAt(query, i, inputTableName))
                {
                    normalized.Append(observableName);
                    i += inputTableName.Length - 1;
                    continue;
                }

                if (IsKeywordAt(query, i, notInAlias))
                {
                    normalized.Append("!in");
                    i += notInAlias.Length - 1;
                    continue;
                }
            }

            normalized.Append(current);
        }

        return RewriteHasAnyExpressions(normalized.ToString());
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        IDisposable[] subscriptions;
        string[] temporaryQueryFiles;
        lock (_gate)
        {
            subscriptions = [.. _subscriptions];
            temporaryQueryFiles = [.. _temporaryQueryFiles];
        }

        foreach (var subscription in subscriptions)
        {
            subscription.Dispose();
        }

        foreach (var temporaryQueryFile in temporaryQueryFiles)
        {
            try { File.Delete(temporaryQueryFile); }
            catch { /* best effort cleanup */ }
        }
    }

    public IObservable<ResourceOutputRecord> Execute(
                    IObservable<SourceEvent> source,
        ResourceProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(profile);

        if (!profile.Enabled)
        {
            return Observable.Empty<ResourceOutputRecord>();
        }

        _ = ScalarFunctionsRegistered.Value;

        return Observable.Create<ResourceOutputRecord>(observer => {
            const string observableName = "Source";
            var inputTableName = string.IsNullOrWhiteSpace(profile.Input.Table) ? observableName : profile.Input.Table;
            var queryPath = CreateTemporaryQueryFile(profile, observableName, inputTableName);
            var disposables = new CompositeDisposable();
            var errorSignaled = 0;
            ResourceMetadata? capturedMetadata = null;

            var kqlRows = new Subject<IDictionary<string, object>>();
            disposables.Add(kqlRows);

            try
            {
                // KqlNodeHub must subscribe to kqlRows before the source starts emitting into it.
                // Otherwise synchronous or finite sources can complete the Subject before the hub sees events.
                var hub = KqlNodeHub.FromFiles(
                    kqlRows,
                    kqlOutput => OnKqlOutput(kqlOutput, profile, observer, capturedMetadata),
                    observableName,
                    queryPath);

                foreach (var failedQuery in hub._node.FailedKqlQueryList)
                {
                    if (Interlocked.Exchange(ref errorSignaled, 1) == 0)
                    {
                        observer.OnError(failedQuery.FailureReason);
                    }
                }

                if (hub._node.FailedKqlQueryList.Count > 0)
                {
                    return disposables;
                }

                hub._node.KqlKqlQueryFailed += (_, args) => {
                    if (Interlocked.Exchange(ref errorSignaled, 1) == 0)
                    {
                        observer.OnError(args.Exception);
                    }
                };
                hub._node.EnableFailedKqlQueryEvents = true;

                if (hub._outputSubscription is not null)
                {
                    lock (_gate)
                    {
                        _subscriptions.Add(hub._outputSubscription);
                    }

                    disposables.Add(hub._outputSubscription);
                }

                var sourceSubscription = source.Subscribe(
                    sourceEvent => {
                        Interlocked.CompareExchange(ref capturedMetadata, sourceEvent.Metadata, null);
                        TryNotifyKqlRows(kqlRows, rows => rows.OnNext(DictionaryCoercion.ToKqlDictionary(sourceEvent.ToKqlRow())));
                    },
                    error => {
                        if (Interlocked.Exchange(ref errorSignaled, 1) == 0)
                        {
                            observer.OnError(error);
                        }

                        TryNotifyKqlRows(kqlRows, rows => rows.OnError(error));
                    },
                    () => TryNotifyKqlRows(kqlRows, rows => rows.OnCompleted()));
                disposables.Add(sourceSubscription);

                disposables.Add(cancellationToken.Register(observer.OnCompleted));
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref errorSignaled, 1) == 0)
                {
                    observer.OnError(ex);
                }
            }

            return disposables;
        });
    }


    private static string RewriteHasAnyExpressions(string query)
    {
        const string hasAnyOperator = "has_any";
        var rewritten = new StringBuilder(query.Length);
        var searchStart = 0;

        while (TryFindKeywordOutsideStrings(query, hasAnyOperator, searchStart, out var operatorIndex))
        {
            var leftStart = FindLeftOperandStart(query, operatorIndex);
            var leftOperand = query[leftStart..operatorIndex].Trim();
            var valuesStart = SkipWhitespace(query, operatorIndex + hasAnyOperator.Length);

            if (leftOperand.Length == 0 || valuesStart >= query.Length || query[valuesStart] != '(' || !TryFindMatchingParenthesis(query, valuesStart, out var valuesEnd))
            {
                rewritten.Append(query, searchStart, operatorIndex + hasAnyOperator.Length - searchStart);
                searchStart = operatorIndex + hasAnyOperator.Length;
                continue;
            }

            var values = SplitTopLevelCommaSeparatedValues(query[(valuesStart + 1)..valuesEnd]);
            if (values.Count == 0)
            {
                rewritten.Append(query, searchStart, valuesEnd + 1 - searchStart);
                searchStart = valuesEnd + 1;
                continue;
            }

            rewritten.Append(query, searchStart, leftStart - searchStart);
            rewritten.Append('(');
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    rewritten.Append(" or ");
                }

                rewritten.Append(leftOperand);
                rewritten.Append(" has ");
                rewritten.Append(values[i]);
            }

            rewritten.Append(')');
            searchStart = valuesEnd + 1;
        }

        rewritten.Append(query, searchStart, query.Length - searchStart);
        return rewritten.ToString();
    }

    private static bool TryFindKeywordOutsideStrings(string text, string keyword, int startIndex, out int keywordIndex)
    {
        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var current = text[i];

            if (current == '\'' && !inDoubleQuotedString)
            {
                if (inSingleQuotedString && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inSingleQuotedString = !inSingleQuotedString;
                continue;
            }

            if (current == '"' && !inSingleQuotedString)
            {
                if (inDoubleQuotedString && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inDoubleQuotedString = !inDoubleQuotedString;
                continue;
            }

            if (!inSingleQuotedString && !inDoubleQuotedString && IsKeywordAt(text, i, keyword))
            {
                keywordIndex = i;
                return true;
            }
        }

        keywordIndex = -1;
        return false;
    }

    private static int FindLeftOperandStart(string text, int operatorIndex)
    {
        var index = operatorIndex - 1;
        while (index >= 0 && char.IsWhiteSpace(text[index]))
        {
            index--;
        }

        var parenDepth = 0;
        for (; index >= 0; index--)
        {
            var current = text[index];
            if (current == ')')
            {
                parenDepth++;
                continue;
            }

            if (current == '(')
            {
                if (parenDepth == 0)
                {
                    return index + 1;
                }

                parenDepth--;
                continue;
            }

            if (parenDepth == 0)
            {
                if (current == '|')
                {
                    return index + 1;
                }

                if (IsLogicalOperatorEndingAt(text, index + 1, "where") || IsLogicalOperatorEndingAt(text, index + 1, "and") || IsLogicalOperatorEndingAt(text, index + 1, "or"))
                {
                    return SkipWhitespace(text, index + 1);
                }
            }
        }

        return 0;
    }

    private static bool IsLogicalOperatorEndingAt(string text, int endIndex, string keyword)
    {
        var startIndex = endIndex - keyword.Length;
        return startIndex >= 0 && IsKeywordAt(text, startIndex, keyword);
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static bool TryFindMatchingParenthesis(string text, int openParenIndex, out int closeParenIndex)
    {
        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;
        var depth = 0;

        for (var i = openParenIndex; i < text.Length; i++)
        {
            var current = text[i];
            if (current == '\'' && !inDoubleQuotedString)
            {
                if (inSingleQuotedString && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inSingleQuotedString = !inSingleQuotedString;
                continue;
            }

            if (current == '"' && !inSingleQuotedString)
            {
                if (inDoubleQuotedString && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inDoubleQuotedString = !inDoubleQuotedString;
                continue;
            }

            if (inSingleQuotedString || inDoubleQuotedString)
            {
                continue;
            }

            if (current == '(')
            {
                depth++;
            }
            else if (current == ')' && --depth == 0)
            {
                closeParenIndex = i;
                return true;
            }
        }

        closeParenIndex = -1;
        return false;
    }

    private static List<string> SplitTopLevelCommaSeparatedValues(string values)
    {
        var result = new List<string>();
        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;
        var depth = 0;
        var start = 0;

        for (var i = 0; i < values.Length; i++)
        {
            var current = values[i];
            if (current == '\'' && !inDoubleQuotedString)
            {
                if (inSingleQuotedString && i + 1 < values.Length && values[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inSingleQuotedString = !inSingleQuotedString;
                continue;
            }

            if (current == '"' && !inSingleQuotedString)
            {
                if (inDoubleQuotedString && i + 1 < values.Length && values[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inDoubleQuotedString = !inDoubleQuotedString;
                continue;
            }

            if (inSingleQuotedString || inDoubleQuotedString)
            {
                continue;
            }

            if (current == '(')
            {
                depth++;
            }
            else if (current == ')')
            {
                depth--;
            }
            else if (current == ',' && depth == 0)
            {
                AddValue(values, start, i, result);
                start = i + 1;
            }
        }

        AddValue(values, start, values.Length, result);
        return result;
    }

    private static void AddValue(string values, int start, int end, List<string> result)
    {
        var value = values[start..end].Trim();
        if (value.Length > 0)
        {
            result.Add(value);
        }
    }

    private static bool IsKeywordAt(string text, int index, string keyword)
    {
        if (index + keyword.Length > text.Length)
        {
            return false;
        }

        if (index > 0 && IsKqlIdentifierCharacter(text[index - 1]))
        {
            return false;
        }

        if (index + keyword.Length < text.Length && IsKqlIdentifierCharacter(text[index + keyword.Length]))
        {
            return false;
        }

        return string.Compare(text, index, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static bool IsKqlIdentifierCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static void OnKqlOutput(KqlOutput kqlOutput, ResourceProfile profile, IObserver<ResourceOutputRecord> output, ResourceMetadata? sourceMetadata)
    {
        try
        {
            var projected = kqlOutput.Output.ToDictionary(k => k.Key, v => (object?)v.Value, StringComparer.OrdinalIgnoreCase);
            var record = ResourceOutputRecord.FromKqlProjection(projected, profile.Id, profile.Version, sourceMetadata);
            output.OnNext(record);
        }
        catch (Exception ex)
        {
            output.OnError(ex);
        }
    }

    private static bool RegisterScalarFunctions()
    {
        ScalarFunctionFactory.AddFunctions(typeof(AgentScalarFunctions));
        return true;
    }

    private static void TryNotifyKqlRows(Subject<IDictionary<string, object>> kqlRows, Action<Subject<IDictionary<string, object>>> notify)
    {
        try
        {
            notify(kqlRows);
        }
        catch (ObjectDisposedException)
        {
            // Downstream error/completion can synchronously dispose the pipeline before the KQL subject is notified.
        }
    }

    private string CreateTemporaryQueryFile(ResourceProfile profile, string observableName, string inputTableName)
    {
        var path = Path.Combine(Path.GetTempPath(), $"agent-{profile.Id}-{Guid.NewGuid():N}.kql");
        var query = string.IsNullOrWhiteSpace(profile.Filter.Query)
            ? inputTableName
            : profile.Filter.Query;

        File.WriteAllText(path, NormalizeQueryForRxKql(query, inputTableName, observableName));
        lock (_gate)
        {
            _temporaryQueryFiles.Add(path);
        }
        return path;
    }
}