using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace DeltaZulu.Agent.Inputs.Syslog;

public sealed class SyslogFileTailInput : IResourceInput
{
    private readonly string _path;
    private readonly TimeSpan _pollInterval;
    private readonly LightweightSyslogParser _parser = new();

    public string Name { get; }

    public SyslogFileTailInput(string path, string name = "syslog-file", TimeSpan? pollInterval = null)
    {
        _path = path;
        Name = name;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(200);
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
                    using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    stream.Seek(0, SeekOrigin.End);

                    while (!cts.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
                        if (line is null)
                        {
                            await Task.Delay(_pollInterval, cts.Token).ConfigureAwait(false);
                            continue;
                        }

                        if (line.Length > 0)
                        {
                            observer.OnNext(_parser.Parse(line, Name));
                        }
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