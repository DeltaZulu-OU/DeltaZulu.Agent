using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace DeltaZulu.Agent.Inputs.Auditd;

public sealed class AuditdFileInput : IResourceInput
{
    private readonly string _path;
    private readonly AuditdRecordParser _parser = new();
    private readonly AuditdEventAssembler _assembler = new();

    public string Name { get; }

    public AuditdFileInput(string path, string name = "auditd-file")
    {
        _path = path;
        Name = name;
    }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default)
    {
        return Observable.Create<SourceEvent>(observer =>
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = Task.Run(async () =>
            {
                try
                {
                    string? previousId = null;
                    await foreach (var line in File.ReadLinesAsync(_path, cts.Token).ConfigureAwait(false))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var record = _parser.Parse(line);
                        if (previousId is not null && !record.Id.Equals(previousId, StringComparison.OrdinalIgnoreCase))
                        {
                            var completed = _assembler.Flush(previousId);
                            if (completed is not null) observer.OnNext(completed);
                        }

                        _assembler.Accept(record);
                        previousId = record.Id;
                    }

                    foreach (var completed in _assembler.FlushAll())
                    {
                        observer.OnNext(completed);
                    }

                    observer.OnCompleted();
                }
                catch (OperationCanceledException)
                {
                    observer.OnCompleted();
                }
                catch (Exception ex)
                {
                    observer.OnError(ex);
                }
            }, cts.Token);

            return Disposable.Create(() => cts.Cancel());
        });
    }
}