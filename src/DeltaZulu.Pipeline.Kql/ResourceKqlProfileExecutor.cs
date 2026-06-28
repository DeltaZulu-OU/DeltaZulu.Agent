using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;
using System.Reactive.Disposables;
using System.Reactive.Kql;
using System.Reactive.Kql.CustomTypes;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;

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
            var queryPath = CreateTemporaryQueryFile(profile);
            var tableName = string.IsNullOrWhiteSpace(profile.Input.Table) ? "Source" : profile.Input.Table;
            var disposables = new CompositeDisposable();
            var errorSignaled = 0;
            ResourceMetadata? capturedMetadata = null;

            var kqlRows = new Subject<IDictionary<string, object>>();
            disposables.Add(kqlRows);

            try
            {
                var sourceSubscription = source.Subscribe(
                    sourceEvent => {
                        Interlocked.CompareExchange(ref capturedMetadata, sourceEvent.Metadata, null);
                        kqlRows.OnNext(DictionaryCoercion.ToKqlDictionary(sourceEvent.ToKqlRow()));
                    },
                    error => {
                        kqlRows.OnError(error);
                        if (Interlocked.Exchange(ref errorSignaled, 1) == 0)
                        {
                            observer.OnError(error);
                        }
                    },
                    kqlRows.OnCompleted);
                disposables.Add(sourceSubscription);

                var hub = KqlNodeHub.FromFiles(
                    kqlRows,
                    kqlOutput => OnKqlOutput(kqlOutput, profile, observer, capturedMetadata),
                    tableName,
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

    private static bool RegisterScalarFunctions()
    {
        ScalarFunctionFactory.AddFunctions(typeof(AgentScalarFunctions));
        return true;
    }

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

    /// <summary>
    /// Normalizes Kusto syntax aliases that are not accepted by Microsoft.Rx.Kql.
    /// </summary>
    public static string NormalizeQueryForRxKql(string query)
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

            if (!inSingleQuotedString
                && !inDoubleQuotedString
                && IsKeywordAt(query, i, notInAlias))
            {
                normalized.Append("!in");
                i += notInAlias.Length - 1;
                continue;
            }

            normalized.Append(current);
        }

        return normalized.ToString();
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

    private string CreateTemporaryQueryFile(ResourceProfile profile)
    {
        var path = Path.Combine(Path.GetTempPath(), $"agent-{profile.Id}-{Guid.NewGuid():N}.kql");
        var query = string.IsNullOrWhiteSpace(profile.Filter.Query)
            ? profile.Input.Table
            : profile.Filter.Query;

        File.WriteAllText(path, NormalizeQueryForRxKql(query));
        lock (_gate)
        {
            _temporaryQueryFiles.Add(path);
        }
        return path;
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
}
