using System.Reactive.Linq;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Inputs.Auditd;

namespace DeltaZulu.Agent.ProfileWorkbench;

internal sealed class AuditdTailSourceInput : ISourceInput
{
    private readonly string _path;
    private readonly TimeSpan _pollInterval;
    private readonly AuditdRecordParser _parser = new();
    private readonly AuditdEventAssembler _assembler = new();

    public AuditdTailSourceInput(string path, string name = "auditd-tail", TimeSpan? pollInterval = null)
    {
        _path = path;
        Name = name;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
    }

    public string Name { get; }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) => Observable.Create<SourceEvent>(observer => {
        return Task.Run(async () => {
            try
            {
                var position = 0L;
                string? previousId = null;

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!File.Exists(_path))
                    {
                        await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (position == 0)
                    {
                        position = stream.Length;
                    }
                    else if (position > stream.Length)
                    {
                        position = 0;
                        previousId = null;
                        foreach (var completed in _assembler.FlushAll())
                        {
                            observer.OnNext(completed);
                        }
                    }

                    stream.Seek(position, SeekOrigin.Begin);
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        AuditdRecord record;
                        try
                        {
                            record = _parser.Parse(line);
                        }
                        catch (FormatException)
                        {
                            continue;
                        }

                        if (previousId is not null && !record.Id.Equals(previousId, StringComparison.OrdinalIgnoreCase))
                        {
                            var completed = _assembler.Flush(previousId);
                            if (completed is not null)
                            {
                                observer.OnNext(completed);
                            }
                        }

                        var result = _assembler.Accept(record);
                        if (result is not null)
                        {
                            observer.OnNext(result);
                        }

                        previousId = record.Id;
                    }

                    position = stream.Position;
                    await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
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
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                observer.OnError(ex);
            }
        }, cancellationToken);
    });
}
