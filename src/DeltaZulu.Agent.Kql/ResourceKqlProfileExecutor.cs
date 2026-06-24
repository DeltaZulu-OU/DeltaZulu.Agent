using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Agent.Profiles;
using System.Reactive.Disposables;
using System.Reactive.Kql;
using System.Reactive.Kql.CustomTypes;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace DeltaZulu.Agent.Kql;

/// <summary>
/// Executes resource profile KQL over SourceEvent rows.
/// This class intentionally keeps KQL as the profile language while preserving source-native fields in NDJSON output.
/// </summary>
public sealed class ResourceKqlProfileExecutor : IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];
    private readonly List<string> _temporaryQueryFiles = [];
    private bool _disposed;

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

        ScalarFunctionFactory.AddFunctions(typeof(ResourceQlScalarFunctions));

        var output = new Subject<ResourceOutputRecord>();
        var queryPath = CreateTemporaryQueryFile(profile);
        var tableName = string.IsNullOrWhiteSpace(profile.Input.Table) ? "Source" : profile.Input.Table;

        var kqlRows = source.Select(e => DictionaryCoercion.ToKqlDictionary(e.ToKqlRow()));

        KqlNodeHub? hub = null;
        try
        {
            hub = KqlNodeHub.FromFiles(
                kqlRows,
                kqlOutput => OnKqlOutput(kqlOutput, profile, output),
                tableName,
                queryPath);

            foreach (var failedQuery in hub._node.FailedKqlQueryList)
            {
                output.OnError(failedQuery.FailureReason);
            }

            if (hub._node.FailedKqlQueryList.Count > 0)
            {
                output.OnCompleted();
                return output;
            }

            hub._node.KqlKqlQueryFailed += (_, args) => output.OnError(args.Exception);
            hub._node.EnableFailedKqlQueryEvents = true;

            if (hub._outputSubscription is not null)
            {
                _subscriptions.Add(hub._outputSubscription);
            }
        }
        catch (Exception ex)
        {
            output.OnError(ex);
            output.OnCompleted();
        }

        cancellationToken.Register(() => output.OnCompleted());

        return Observable.Create<ResourceOutputRecord>(observer =>
        {
            var subscription = output.Subscribe(observer);
            return Disposable.Create(() => subscription.Dispose());
        });
    }

    private void OnKqlOutput(KqlOutput kqlOutput, ResourceProfile profile, IObserver<ResourceOutputRecord> output)
    {
        try
        {
            var projected = kqlOutput.Output.ToDictionary(k => k.Key, v => (object?)v.Value, StringComparer.OrdinalIgnoreCase);
            var record = ResourceOutputRecord.FromKqlProjection(projected, profile.Id, profile.Version);
            output.OnNext(record);
        }
        catch (Exception ex)
        {
            output.OnError(ex);
        }
    }

    private string CreateTemporaryQueryFile(ResourceProfile profile)
    {
        var path = Path.Combine(Path.GetTempPath(), $"resourceql-{profile.Id}-{Guid.NewGuid():N}.kql");
        File.WriteAllText(path, profile.Filter.Query);
        _temporaryQueryFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        foreach (var temporaryQueryFile in _temporaryQueryFiles)
        {
            try { File.Delete(temporaryQueryFile); }
            catch { /* best effort cleanup */ }
        }
    }
}