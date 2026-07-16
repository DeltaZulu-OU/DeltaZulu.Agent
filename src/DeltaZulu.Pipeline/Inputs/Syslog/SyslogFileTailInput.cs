using System.Reactive.Disposables;
using System.Reactive.Linq;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Inputs.Syslog;

public sealed class SyslogFileTailInput : ISourceInput
{
    private const int FileBufferSize = 64 * 1024;

    private readonly LightweightSyslogParser _parser = new();
    private readonly string _path;
    private readonly TimeSpan _pollInterval;

    public SyslogFileTailInput(string path, string name = "syslog-file", TimeSpan? pollInterval = null)
    {
        _path = path;
        Name = name;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(200);
    }

    public string Name { get; }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) => Observable.Create<SourceEvent>(observer => {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        FileStream stream;
        StreamReader reader;

        try
        {
            stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileBufferSize,
                FileOptions.SequentialScan | FileOptions.Asynchronous);
            reader = new StreamReader(
                stream,
                encoding: System.Text.Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: FileBufferSize,
                leaveOpen: false);
            stream.Seek(0, SeekOrigin.End);
        }
        catch (Exception ex)
        {
            cts.Dispose();
            observer.OnError(ex);
            return Disposable.Empty;
        }

        _ = Task.Run(async () => {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
                    if (line is null)
                    {
                        if (stream.Position > stream.Length)
                        {
                            reader.DiscardBufferedData();
                            stream.Seek(0, SeekOrigin.Begin);
                        }

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
            finally
            {
                reader.Dispose();
                cts.Dispose();
            }
        }, CancellationToken.None);

        return Disposable.Create(() => { cts.Cancel(); });
    });
}
