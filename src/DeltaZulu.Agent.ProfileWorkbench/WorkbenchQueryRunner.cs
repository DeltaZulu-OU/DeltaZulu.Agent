using System.Reactive.Linq;
using DeltaZulu.Agent.Filter.Kql;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Agent.ProfileWorkbench;

public sealed class WorkbenchQueryRunner
{
    public WorkbenchRunResult RunOnce(WorkbenchRunRequest request, TimeSpan timeout)
    {
        var validation = WorkbenchQueryValidator.Validate(request.Query, request.Source.Schema);
        if (!validation.IsValid)
        {
            return new WorkbenchRunResult([], new WorkbenchCounters(0, 0, 0, 0, null), validation.Error);
        }

        using var cts = new CancellationTokenSource(timeout);
        using var executor = new ResourceKqlProfileExecutor();
        using var completed = new ManualResetEventSlim();

        var profile = CloneProfileForQuery(request.Document.Profile, request.Query, request.Source.Table);
        var rows = new List<IReadOnlyDictionary<string, object?>>(Math.Min(request.RowLimit, 128));
        Exception? error = null;
        var read = 0L;
        var matched = 0L;
        DateTimeOffset? lastEvent = null;
        var truncated = false;

        var source = request.Source.Input.Open(cts.Token).Do(sourceEvent => {
            read++;
            lastEvent = DateTimeOffset.UtcNow;
        });

        using var subscription = executor.Execute(source, profile, cts.Token)
            .Take(request.RowLimit + 1)
            .Subscribe(
                record => {
                    matched++;
                    if (rows.Count < request.RowLimit)
                    {
                        rows.Add(record.Event);
                    }
                    else
                    {
                        truncated = true;
                    }
                },
                ex => { error = ex; completed.Set(); },
                () => completed.Set());

        completed.Wait(timeout);
        if (!completed.IsSet && error is null)
        {
            cts.Cancel();
            error = new TimeoutException("workbench query did not complete before the timeout.");
        }

        return error is null
            ? new WorkbenchRunResult(rows, new WorkbenchCounters(read, matched, 0, rows.Count, lastEvent), Truncated: truncated)
            : new WorkbenchRunResult(rows, new WorkbenchCounters(read, matched, 1, rows.Count, lastEvent), error.GetBaseException().Message, truncated);
    }

    public IDisposable RunLive(WorkbenchRunRequest request, Action<ResourceOutputRecord> onRow, Action<WorkbenchCounters> onCounters, Action<Exception> onError, CancellationToken cancellationToken)
    {
        var validation = WorkbenchQueryValidator.Validate(request.Query, request.Source.Schema);
        if (!validation.IsValid)
        {
            onError(new InvalidOperationException(validation.Error));
            return NoopDisposable.Instance;
        }

        var executor = new ResourceKqlProfileExecutor();
        var profile = CloneProfileForQuery(request.Document.Profile, request.Query, request.Source.Table);
        var read = 0L;
        var matched = 0L;
        var errors = 0L;
        DateTimeOffset? lastEvent = null;

        var source = request.Source.Input.Open(cancellationToken).Do(_ => {
            read++;
            lastEvent = DateTimeOffset.UtcNow;
            onCounters(new WorkbenchCounters(read, matched, errors, matched, lastEvent));
        });

        var subscription = executor.Execute(source, profile, cancellationToken).Subscribe(
            record => {
                matched++;
                onRow(record);
                onCounters(new WorkbenchCounters(read, matched, errors, matched, lastEvent));
            },
            ex => {
                errors++;
                onCounters(new WorkbenchCounters(read, matched, errors, matched, lastEvent));
                onError(ex);
            });

        return new CompositeDisposableAdapter(subscription, executor);
    }

    private static ResourceProfile CloneProfileForQuery(ResourceProfile source, string query, string table) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Id = source.Id,
        Name = source.Name,
        Version = source.Version,
        Enabled = true,
        Mandatory = source.Mandatory,
        Resource = source.Resource,
        Input = new ResourceInputContract { Table = table, Schema = source.Input.Schema },
        Output = new ResourceOutputContract { Format = "table", PreserveOriginalFieldNames = true, PreserveRawEvent = source.Output.PreserveRawEvent },
        Filter = new ResourceFilter { Language = "kql", Query = query },
        Condition = source.Condition
    };

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }

    private sealed class CompositeDisposableAdapter : IDisposable
    {
        private readonly IDisposable _subscription;
        private readonly IDisposable _executor;

        public CompositeDisposableAdapter(IDisposable subscription, IDisposable executor)
        {
            _subscription = subscription;
            _executor = executor;
        }

        public void Dispose()
        {
            _subscription.Dispose();
            _executor.Dispose();
        }
    }
}
